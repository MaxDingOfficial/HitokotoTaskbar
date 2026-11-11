using System.Net;
using System.Net.Http.Headers;

namespace HitokotoTaskbar.Core;

public enum UpdateStatus { Current, Available, Unavailable }

public sealed record GitHubRelease(Version Version, Uri DownloadPage)
{
    public bool ShouldOpenAutomatically(string? lastOpenedVersion) =>
        !System.Version.TryParse(lastOpenedVersion, out var last) || Version > last;
}

public sealed record UpdateResult(UpdateStatus Status, GitHubRelease? Release = null, string? Error = null, Version? LatestVersion = null);

public sealed class GitHubUpdateService : IDisposable
{
    private const string Repository = "MaxDingOfficial/HitokotoTaskbar";
    private const string TagPath = $"/{Repository}/releases/tag/";
    public static readonly Uri LatestReleaseEndpoint = new($"https://github.com/{Repository}/releases/latest");
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public GitHubUpdateService(HttpClient? client = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    }

    public async Task<UpdateResult> CheckAsync(Version installed, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            // GitHub's documented latest-release URL redirects to the latest stable tag.
            // A HEAD request reads just that redirect without consuming the REST API quota.
            using var request = new HttpRequestMessage(HttpMethod.Head, LatestReleaseEndpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HitokotoTaskbar", installed.ToString()));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return new(UpdateStatus.Unavailable, Error: $"GitHub HTTP {(int)response.StatusCode}");
            if (response.Headers.Location is not { } location ||
                !Uri.TryCreate(LatestReleaseEndpoint, location, out var target) || target.Scheme != "https" ||
                !target.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !target.IsDefaultPort ||
                target.UserInfo.Length != 0 || target.Query.Length != 0 || target.Fragment.Length != 0 ||
                !target.AbsolutePath.StartsWith(TagPath, StringComparison.OrdinalIgnoreCase))
                return new(UpdateStatus.Unavailable, Error: "GitHub 未返回此仓库的正式发布地址。");

            var tag = target.AbsolutePath[TagPath.Length..];
            var prefix = tag.StartsWith('v') ? "v" : "";
            if (!Version.TryParse(tag[prefix.Length..], out var latest) || latest.Build < 0 || latest.Revision >= 0 ||
                tag != prefix + latest.ToString(3))
                return new(UpdateStatus.Unavailable, Error: "GitHub 版本标签不是三段式正式版本。");
            if (latest <= installed) return new(UpdateStatus.Current, LatestVersion: latest);

            // Use a canonical page for this exact repo and tag, never a URL supplied by the response.
            return new(UpdateStatus.Available, new(latest, new Uri($"https://github.com{TagPath}{tag}")), LatestVersion: latest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(UpdateStatus.Unavailable, Error: "连接 GitHub 超时。"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        { return new(UpdateStatus.Unavailable, Error: ex.Message); }
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
