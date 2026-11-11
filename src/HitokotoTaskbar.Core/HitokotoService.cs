using System.Net;
using System.Text.Json;

namespace HitokotoTaskbar.Core;

public enum FetchStatus { Success, Throttled, Failed }

public sealed record FetchResult(FetchStatus Status, HitokotoQuote? Quote = null, string? Error = null);

/// <summary>One reusable client, bounded responses, asynchronous I/O and a shared request cooldown.</summary>
public sealed class HitokotoService : IDisposable
{
    public static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    public const int MaximumResponseBytes = 64 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private long _lastRequestTimestamp;
    private bool _hasRequested;
    private bool _requestActive;
    private bool _disposed;
    private TimeSpan _cooldown = MinimumRequestInterval;

    public HitokotoService(HttpClient? client = null, TimeProvider? timeProvider = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            MaxConnectionsPerServer = 1
        }) { Timeout = Timeout.InfiniteTimeSpan };
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<FetchResult> FetchAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked;
        lock (_gate)
        {
            if (_disposed) return new(FetchStatus.Failed, Error: "请求服务已经退出。");
            if (cancellationToken.IsCancellationRequested) return new(FetchStatus.Failed, Error: "请求已取消。");
            if (_requestActive || (_hasRequested && _time.GetElapsedTime(_lastRequestTimestamp) < _cooldown))
                return new(FetchStatus.Throttled, Error: "请求过于频繁，请稍后再试。");
            _requestActive = true;
            _hasRequested = true;
            _lastRequestTimestamp = _time.GetTimestamp();
            _cooldown = MinimumRequestInterval;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            linked.CancelAfter(RequestTimeout);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(settings));
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("HitokotoTaskbar/1.0");
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date is { } date ? date - _time.GetUtcNow() : TimeSpan.FromMinutes(1));
                lock (_gate)
                {
                    // Retry-After starts at the response, not at the earlier request start.
                    _lastRequestTimestamp = _time.GetTimestamp();
                    _cooldown = retry > MinimumRequestInterval ? retry : MinimumRequestInterval;
                }
                return new(FetchStatus.Failed, Error: "一言 API 暂时限流，请稍后再试。");
            }
            if (!response.IsSuccessStatusCode)
                return new(FetchStatus.Failed, Error: $"一言 API 返回 HTTP {(int)response.StatusCode}。");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                return new(FetchStatus.Failed, Error: "一言 API 响应过大。");

            await using var input = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int length;
            while ((length = await input.ReadAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + length > MaximumResponseBytes)
                    return new(FetchStatus.Failed, Error: "一言 API 响应过大。");
                output.Write(buffer, 0, length);
            }
            using var json = JsonDocument.Parse(output.GetBuffer().AsMemory(0, (int)output.Length), new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(FetchStatus.Failed, Error: "一言 API 返回的 JSON 不是对象。");
            var text = ReadString(root, "hitokoto", 8192);
            if (string.IsNullOrWhiteSpace(text))
                return new(FetchStatus.Failed, Error: "一言 API 未返回有效语句。");
            return new(FetchStatus.Success, new HitokotoQuote
            {
                Text = text.Trim(),
                Source = ReadString(root, "from", 2048),
                Author = ReadString(root, "from_who", 1024),
                Uuid = ReadString(root, "uuid", 128),
                FetchedAt = _time.GetUtcNow()
            });
        }
        catch (OperationCanceledException)
        {
            return new(FetchStatus.Failed, Error: cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested
                ? "请求已取消。" : "请求超时，继续显示缓存语句。");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException or InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            return new(FetchStatus.Failed, Error: $"获取一言失败：{error.Message}");
        }
        finally
        {
            linked.Dispose();
            lock (_gate) _requestActive = false;
        }
    }

    /// <summary>Applies the saved filters and preserves unrelated custom query fields.</summary>
    public static Uri BuildRequestUri(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!AppSettings.IsSupportedApiUrl(settings.ApiUrl))
            throw new ArgumentException("API 地址必须是有效的 HTTP 或 HTTPS 地址。", nameof(settings));
        var validated = settings.Validated();
        var uri = new Uri(validated.ApiUrl);
        var builder = new UriBuilder(uri) { Fragment = "" };
        var fields = new List<string>();
        foreach (var (key, _, raw) in AppSettings.QueryFields(uri))
        {
            if (!key.Equals("c", StringComparison.OrdinalIgnoreCase) && !key.Equals("encode", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("min_length", StringComparison.OrdinalIgnoreCase) && !key.Equals("max_length", StringComparison.OrdinalIgnoreCase))
                fields.Add(raw);
        }
        fields.Add("encode=json");
        fields.Add(FormattableString.Invariant($"min_length={validated.MinimumSentenceLength}"));
        fields.Add(FormattableString.Invariant($"max_length={validated.MaximumSentenceLength}"));
        foreach (var category in validated.Categories)
            fields.Add($"c={category}");
        builder.Query = string.Join('&', fields);
        return builder.Uri;
    }

    private static string? ReadString(JsonElement root, string name, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        return text?.Length > maximumLength ? null : text;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            if (_ownsClient) _client.Dispose();
            // The CTS is disposed after cancellation; its token remains usable by pending linked requests.
            _lifetime.Dispose();
        }
    }
}
