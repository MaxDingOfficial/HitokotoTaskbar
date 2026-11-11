using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Tests;

public sealed class HitokotoServiceTests
{
    private const string ValidJson = """{"hitokoto":"生活明朗，万物可爱。","from":"人间","from_who":"作者","uuid":"a6fcf785-7335-4733-878b-4dfe8a23af99"}""";

    [Fact]
    public async Task ParsesChineseSentenceAndNullableCreditWithoutEscapingLoss()
    {
        using var client = Client((_, _) => Task.FromResult(Json(ValidJson)));
        var clock = new ManualTimeProvider();
        using var service = new HitokotoService(client, clock);
        var result = await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken);
        Assert.Equal(FetchStatus.Success, result.Status);
        Assert.NotNull(result.Quote);
        Assert.Equal("生活明朗，万物可爱。", result.Quote.Text);
        Assert.Equal("人间", result.Quote.Source);
        Assert.Equal("作者", result.Quote.Author);
        Assert.Equal("a6fcf785-7335-4733-878b-4dfe8a23af99", result.Quote.Uuid);
        Assert.Equal(clock.GetUtcNow(), result.Quote.FetchedAt);
        Assert.Equal("生活明朗，万物可爱。\n—— 作者 · 人间", result.Quote.TooltipText);
    }

    [Fact]
    public void QueryUsesSavedFiltersInsteadOfConflictingUrlParameters()
    {
        var uri = HitokotoService.BuildRequestUri(new AppSettings
        {
            ApiUrl = "https://v1.hitokoto.cn/?MAX_LENGTH=28&max_length=99&min%5Flength=3&c=a&c=d&encode=text&x=%E4%B8%AD#fragment",
            Categories = "kkB,b l!m", MinimumSentenceLength = 8, MaximumSentenceLength = 18
        });
        Assert.Equal("?x=%E4%B8%AD&encode=json&min_length=8&max_length=18&c=k&c=b&c=l", uri.Query);
        Assert.Equal("", uri.Fragment);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"hitokoto\":\"   \"}")]
    [InlineData("{\"hitokoto\":42}")]
    public async Task RejectsInvalidResponsesAndKeepsCachedSettingsUntouched(string body)
    {
        var settings = new AppSettings { LastQuote = new() { Text = "上一条有效语句" } };
        using var client = Client((_, _) => Task.FromResult(Json(body)));
        using var service = new HitokotoService(client);
        var result = await service.FetchAsync(settings, TestContext.Current.CancellationToken);
        Assert.Equal(FetchStatus.Failed, result.Status);
        Assert.Null(result.Quote);
        Assert.Equal("上一条有效语句", settings.LastQuote.Text);
    }

    [Fact]
    public async Task FastManualClicksOnlySendOneRequestAndResumeAfterCooldown()
    {
        var count = 0;
        using var client = Client((_, _) => { count++; return Task.FromResult(Json(ValidJson)); });
        var clock = new ManualTimeProvider();
        using var service = new HitokotoService(client, clock);
        Assert.Equal(FetchStatus.Success, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        for (var i = 0; i < 10; i++)
            Assert.Equal(FetchStatus.Throttled, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, count);
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(FetchStatus.Success, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task FiveMinutesAllowAnotherRequestWithoutClockWaiting()
    {
        var clock = new ManualTimeProvider();
        using var client = Client((_, _) => Task.FromResult(Json(ValidJson)));
        using var service = new HitokotoService(client, clock);
        var settings = new AppSettings();
        Assert.Equal(5, settings.RefreshIntervalMinutes);
        Assert.Equal(FetchStatus.Success, (await service.FetchAsync(settings, TestContext.Current.CancellationToken)).Status);
        clock.Advance(TimeSpan.FromMinutes(settings.RefreshIntervalMinutes));
        Assert.Equal(FetchStatus.Success, (await service.FetchAsync(settings, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task ConcurrentRefreshDoesNotIssueSecondNetworkRequest()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var client = Client((_, _) => { count++; return gate.Task; });
        using var service = new HitokotoService(client);
        var first = service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken);
        Assert.Equal(FetchStatus.Throttled, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, count);
        gate.SetResult(Json(ValidJson));
        Assert.Equal(FetchStatus.Success, (await first).Status);
    }

    [Fact]
    public async Task NetworkFailureIsContainedAndRateLimited()
    {
        using var client = Client((_, _) => throw new HttpRequestException("offline"));
        using var service = new HitokotoService(client);
        var result = await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken);
        Assert.Equal(FetchStatus.Failed, result.Status);
        Assert.Contains("offline", result.Error);
        Assert.Equal(FetchStatus.Throttled, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task CancellationCompletesWithoutUnhandledException()
    {
        using var client = Client(async (_, cancellation) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            return Json(ValidJson);
        });
        using var service = new HitokotoService(client);
        using var cancellation = new CancellationTokenSource();
        var request = service.FetchAsync(new AppSettings(), cancellation.Token);
        cancellation.Cancel();
        Assert.Equal(FetchStatus.Failed, (await request).Status);
    }

    [Fact]
    public async Task DisposalCancelsInFlightRequestAndRejectsFutureCalls()
    {
        using var client = Client(async (_, cancellation) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            return Json(ValidJson);
        });
        var service = new HitokotoService(client);
        var request = service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken);
        service.Dispose();
        Assert.Equal(FetchStatus.Failed, (await request).Status);
        Assert.Equal(FetchStatus.Failed, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        service.Dispose();
    }

    [Fact]
    public async Task TooManyRequestsHonorsRetryAfter()
    {
        var clock = new ManualTimeProvider();
        var count = 0;
        using var client = Client((_, _) =>
        {
            count++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return Task.FromResult(response);
        });
        using var service = new HitokotoService(client, clock);
        Assert.Equal(FetchStatus.Failed, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(FetchStatus.Throttled, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(FetchStatus.Failed, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RetryAfterStartsWhenSlowResponseArrives()
    {
        var clock = new ManualTimeProvider();
        using var client = Client((_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return Task.FromResult(response);
        });
        using var service = new HitokotoService(client, clock);
        Assert.Equal(FetchStatus.Failed, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(FetchStatus.Throttled, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FetchStatus.Failed, (await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task NullAuthorIsValidAndDoesNotCreateEmptyCreditSeparator()
    {
        using var client = Client((_, _) => Task.FromResult(Json("""{"hitokoto":"你好。","from":"测试","from_who":null,"uuid":"test"}""")));
        using var service = new HitokotoService(client);
        var result = await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken);
        Assert.Equal(FetchStatus.Success, result.Status);
        Assert.Equal("你好。\n—— 测试", result.Quote?.TooltipText);
    }

    [Fact]
    public async Task RejectsOversizedResponseWithDeclaredLength()
    {
        using var client = Client((_, _) => Task.FromResult(Json(new string('x', HitokotoService.MaximumResponseBytes + 1))));
        using var service = new HitokotoService(client);
        var result = await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken);
        Assert.Equal(FetchStatus.Failed, result.Status);
        Assert.Contains("过大", result.Error);
    }

    [Fact]
    public async Task RejectsOversizedChunkedResponseWithoutContentLength()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[HitokotoService.MaximumResponseBytes + 1])
        }));
        using var service = new HitokotoService(client);
        var result = await service.FetchAsync(new AppSettings(), TestContext.Current.CancellationToken);
        Assert.Equal(FetchStatus.Failed, result.Status);
        Assert.Contains("过大", result.Error);
    }

    [Theory]
    [InlineData("file:///C:/private.json")]
    [InlineData("https://user:password@v1.hitokoto.cn/")]
    [InlineData("not a uri")]
    public void RejectsInvalidApiUri(string uri) =>
        Assert.Throws<ArgumentException>(() => HitokotoService.BuildRequestUri(new AppSettings { ApiUrl = uri }));

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        new(new StubHandler(responder));

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public void Advance(TimeSpan value) => _ticks += value.Ticks;
    }
}
