using System.Globalization;
using System.Text.RegularExpressions;

namespace HitokotoTaskbar.Core;

public sealed partial class AppSettings
{
    public const string DefaultApiUrl = "https://v1.hitokoto.cn/";
    public const int DefaultMaximumSentenceLength = 28;
    public const int SentenceLengthLimit = 8192;
    public string ApiUrl { get; set; } = DefaultApiUrl;
    public string Categories { get; set; } = "";
    // Null marks an older settings file whose bounds may still be in the API URL.
    public int? MinimumSentenceLength { get; set; }
    public int? MaximumSentenceLength { get; set; }
    public double RefreshIntervalMinutes { get; set; } = 5;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 13;
    public string TextColor { get; set; } = "#F5EAF8";
    public double Opacity { get; set; } = 1;
    public double HorizontalOffset { get; set; } = 24;
    public double VerticalOffset { get; set; }
    public double MaxWidth { get; set; } = 500;
    public string MonitorDeviceName { get; set; } = "";
    public bool ShowOnAllDisplays { get; set; }
    public bool AutomaticallyCheckForUpdates { get; set; } = true;
    public string? LastOpenedUpdateVersion { get; set; }
    public bool ShowQuoteIcon { get; set; } = true;
    public bool EnableTransitions { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public bool AutoStart { get; set; }
    public HitokotoQuote? LastQuote { get; set; }

    public AppSettings Copy() => (AppSettings)MemberwiseClone();

    /// <summary>Returns a safe copy; malformed user-editable fields never reach WPF or timers.</summary>
    public AppSettings Validated()
    {
        var copy = Copy();
        copy.ApiUrl = IsSupportedApiUrl(ApiUrl) ? ApiUrl.Trim() : DefaultApiUrl;
        copy.Categories = new string((Categories ?? "").ToLowerInvariant()
            .Where(character => character is >= 'a' and <= 'l').Distinct().ToArray());
        int? legacyMin = null, legacyMax = null;
        if (MinimumSentenceLength is null || MaximumSentenceLength is null)
        {
            foreach (var (key, rawValue, _) in QueryFields(new Uri(copy.ApiUrl)))
            {
                if (!int.TryParse(Uri.UnescapeDataString(rawValue), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
                    continue;
                if (key.Equals("min_length", StringComparison.OrdinalIgnoreCase)) legacyMin ??= length;
                if (key.Equals("max_length", StringComparison.OrdinalIgnoreCase)) legacyMax ??= length;
            }
        }
        var maximumLength = Math.Clamp(MaximumSentenceLength ?? legacyMax ?? DefaultMaximumSentenceLength, 1, SentenceLengthLimit);
        var minimumLength = MinimumSentenceLength ?? legacyMin ?? 0;
        copy.MinimumSentenceLength = minimumLength >= 0 && minimumLength <= maximumLength ? minimumLength : 0;
        copy.MaximumSentenceLength = maximumLength;
        copy.RefreshIntervalMinutes = Clamp(RefreshIntervalMinutes, 1, 1440, 5);
        copy.FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Microsoft YaHei UI" : FontFamily.Trim()[..Math.Min(FontFamily.Trim().Length, 256)];
        copy.FontSize = Clamp(FontSize, 8, 48, 13);
        copy.TextColor = ColorRegex().IsMatch(TextColor ?? "") ? TextColor! : "#F5EAF8";
        copy.Opacity = Clamp(Opacity, 0.1, 1, 1);
        copy.HorizontalOffset = Clamp(HorizontalOffset, 0, 2000, 24);
        copy.VerticalOffset = Clamp(VerticalOffset, -100, 100, 0);
        copy.MaxWidth = Clamp(MaxWidth, 80, 2000, 500);
        copy.MonitorDeviceName = (MonitorDeviceName ?? "").Trim();
        if (copy.LastQuote is { } quote && (string.IsNullOrWhiteSpace(quote.Text) || quote.Text.Length > 8192))
            copy.LastQuote = null;
        return copy;
    }

    public static bool IsSupportedApiUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

    internal static IEnumerable<(string Key, string RawValue, string Raw)> QueryFields(Uri uri)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            var key = Uri.UnescapeDataString((equals < 0 ? part : part[..equals]).Replace('+', ' '));
            yield return (key, equals < 0 ? "" : part[(equals + 1)..], part);
        }
    }

    private static double Clamp(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();
}
