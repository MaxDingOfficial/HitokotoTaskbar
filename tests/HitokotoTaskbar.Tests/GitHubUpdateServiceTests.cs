using System.Net;
using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task NewerReleaseUsesCanonicalGitHubPageAndOpensAutomaticallyOnlyOnce()
    {
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal(GitHubUpdateService.LatestReleaseEndpoint, request.RequestUri);
            Assert.Equal(HttpMethod.Head, request.Method);
            Assert.NotEmpty(request.Headers.UserAgent);
            Assert.Null(request.Headers.Authorization);
            return Redirect("/MaxDingOfficial/HitokotoTaskbar/releases/tag/v1.10.0");
        }));
        using var service = new GitHubUpdateService(client);

        var update = await service.CheckAsync(new Version(1, 2, 0), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Available, update.Status);
        var release = Assert.IsType<GitHubRelease>(update.Release);
        Assert.Equal("https://github.com/MaxDingOfficial/HitokotoTaskbar/releases/tag/v1.10.0", release.DownloadPage.AbsoluteUri);
        Assert.True(release.ShouldOpenAutomatically("1.9.0"));
        Assert.False(release.ShouldOpenAutomatically("1.10.0"));
        Assert.False(release.ShouldOpenAutomatically("2.0.0"));
    }

    [Theory]
    [InlineData("v1.2.0")]
    [InlineData("v1.1.9")]
    public async Task NoBrowserDestinationForSameOrOlderRelease(string tag)
    {
        using var client = new HttpClient(new Handler(_ => Redirect($"https://github.com/MaxDingOfficial/HitokotoTaskbar/releases/tag/{tag}")));
        using var service = new GitHubUpdateService(client);
        var update = await service.CheckAsync(new Version(1, 2, 0), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Current, update.Status);
        Assert.Null(update.Release);
        Assert.Equal(Version.Parse(tag[1..]), update.LatestVersion);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.Found, "https://example.invalid/MaxDingOfficial/HitokotoTaskbar/releases/tag/v2.0.0")]
    [InlineData(HttpStatusCode.Found, "https://github.com/Someone/AnotherApp/releases/tag/v2.0.0")]
    [InlineData(HttpStatusCode.Found, "https://github.com/login")]
    [InlineData(HttpStatusCode.Found, "/MaxDingOfficial/HitokotoTaskbar/releases/tag/v2.0.0-beta.1")]
    public async Task UnexpectedRedirectOrFailedRequestDoesNotSupplyABrowserDestination(HttpStatusCode status, string? location)
    {
        using var client = new HttpClient(new Handler(_ => location is null ? new(status) : Redirect(location)));
        using var service = new GitHubUpdateService(client);
        var update = await service.CheckAsync(new Version(1, 2, 0), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateStatus.Unavailable, update.Status);
        Assert.Null(update.Release);
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
