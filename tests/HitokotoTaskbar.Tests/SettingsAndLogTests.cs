using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Tests;

public sealed class SettingsAndLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HitokotoTaskbar.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultSettingsAreUsableAndAutoStartIsOptIn()
    {
        var settings = Store().Load();
        Assert.Equal(AppSettings.DefaultApiUrl, settings.ApiUrl);
        Assert.Equal(0, settings.MinimumSentenceLength);
        Assert.Equal(28, settings.MaximumSentenceLength);
        Assert.Equal(5, settings.RefreshIntervalMinutes);
        Assert.Equal("Microsoft YaHei UI", settings.FontFamily);
        Assert.Equal(13, settings.FontSize);
        Assert.False(settings.AutoStart);
        Assert.True(settings.ShowTrayIcon);
    }

    [Fact]
    public void RoundTripRetainsChineseCacheAndAllUserSettings()
    {
        var store = Store();
        var settings = new AppSettings
        {
            Categories = "abk", MinimumSentenceLength = 8, MaximumSentenceLength = 18,
            FontFamily = "微软雅黑", FontSize = 15, TextColor = "#AA88CC", Opacity = 0.8,
            RefreshIntervalMinutes = 8, HorizontalOffset = 60, VerticalOffset = -2, MaxWidth = 350,
            MonitorDeviceName = "\\\\.\\DISPLAY2", ShowQuoteIcon = false, EnableTransitions = true,
            ShowTrayIcon = false, AutoStart = true,
            LastQuote = new() { Text = "你好，世界。", Source = "测试出处", Author = "测试作者", Uuid = "uuid", FetchedAt = DateTimeOffset.UtcNow }
        };
        store.Save(settings);
        var loaded = store.Load();
        Assert.Equal(settings.LastQuote, loaded.LastQuote);
        Assert.Equal(settings.FontFamily, loaded.FontFamily);
        Assert.Equal(settings.FontSize, loaded.FontSize);
        Assert.Equal(settings.TextColor, loaded.TextColor);
        Assert.Equal(settings.Opacity, loaded.Opacity);
        Assert.Equal(settings.Categories, loaded.Categories);
        Assert.Equal(settings.MinimumSentenceLength, loaded.MinimumSentenceLength);
        Assert.Equal(settings.MaximumSentenceLength, loaded.MaximumSentenceLength);
        Assert.Equal(settings.RefreshIntervalMinutes, loaded.RefreshIntervalMinutes);
        Assert.Equal(settings.HorizontalOffset, loaded.HorizontalOffset);
        Assert.Equal(settings.VerticalOffset, loaded.VerticalOffset);
        Assert.Equal(settings.MaxWidth, loaded.MaxWidth);
        Assert.Equal(settings.MonitorDeviceName, loaded.MonitorDeviceName);
        Assert.Equal(settings.ShowQuoteIcon, loaded.ShowQuoteIcon);
        Assert.Equal(settings.EnableTransitions, loaded.EnableTransitions);
        Assert.Equal(settings.ShowTrayIcon, loaded.ShowTrayIcon);
        Assert.Equal(settings.AutoStart, loaded.AutoStart);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void LegacyUrlBoundsMigrateAndAnEmptySelectionRequestsAllCategories()
    {
        var store = Store();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, """{"apiUrl":"https://v1.hitokoto.cn/?min_length=8&max_length=18&lang=zh","categories":"di","refreshIntervalMinutes":10}""");
        var settings = store.Load();
        Assert.Equal("di", settings.Categories);
        Assert.Equal(8, settings.MinimumSentenceLength);
        Assert.Equal(18, settings.MaximumSentenceLength);
        Assert.Equal(10, settings.RefreshIntervalMinutes);
        settings.Categories = "";
        store.Save(settings);
        var request = HitokotoService.BuildRequestUri(store.Load());
        Assert.Equal("?lang=zh&encode=json&min_length=8&max_length=18", request.Query);
    }

    [Fact]
    public void InvalidStoredBoundsFallBackToAnOrderedRange()
    {
        var settings = new AppSettings { MinimumSentenceLength = 40, MaximumSentenceLength = -10 }.Validated();
        Assert.Equal(0, settings.MinimumSentenceLength);
        Assert.Equal(1, settings.MaximumSentenceLength);
    }

    [Fact]
    public void CorruptConfigIsBackedUpLoggedAndReplacedByDefaults()
    {
        var store = Store();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, "{ broken config");
        Assert.Equal(5, store.Load().RefreshIntervalMinutes);
        Assert.Equal("{ broken config", File.ReadAllText(store.FilePath + ".corrupt"));
        Assert.Contains("读取配置失败", File.ReadAllText(Path.Combine(_directory, "logs", "app.log")));
        Assert.Equal(5, store.Load().RefreshIntervalMinutes);
        Assert.Contains("refreshIntervalMinutes", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void ValidationClampsUnsafeFieldsWithoutMutatingOriginal()
    {
        var settings = new AppSettings
        {
            ApiUrl = "javascript:alert(1)", Categories = "abc-A xyzlk", RefreshIntervalMinutes = 0,
            FontSize = double.NaN, Opacity = 12, MaxWidth = -5, HorizontalOffset = -999, VerticalOffset = 999,
            FontFamily = " ", TextColor = "garbage", LastQuote = new() { Text = " " }
        };
        var valid = settings.Validated();
        Assert.Equal(AppSettings.DefaultApiUrl, valid.ApiUrl);
        Assert.Equal("abclk", valid.Categories);
        Assert.Equal(1, valid.RefreshIntervalMinutes);
        Assert.Equal(13, valid.FontSize);
        Assert.Equal(1, valid.Opacity);
        Assert.Equal(80, valid.MaxWidth);
        Assert.Equal(0, valid.HorizontalOffset);
        Assert.Equal(100, valid.VerticalOffset);
        Assert.Equal("Microsoft YaHei UI", valid.FontFamily);
        Assert.Equal("#F5EAF8", valid.TextColor);
        Assert.Null(valid.LastQuote);
        Assert.Equal(-999, settings.HorizontalOffset);
    }

    [Fact]
    public void RepeatedAtomicSavesLeaveOneValidFileAndNoTemporaries()
    {
        var store = Store();
        for (var index = 1; index <= 20; index++)
            store.Save(new AppSettings { LastQuote = new() { Text = $"语句 {index}" } });
        Assert.Equal("语句 20", store.Load().LastQuote?.Text);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void LoggingRotatesAndLimitsOversizedEntries()
    {
        var log = new AppLog(_directory);
        for (var index = 0; index < 200; index++) log.Write(new string('中', 20_000));
        var files = Directory.GetFiles(_directory);
        Assert.Equal(3, files.Length);
        Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, AppLog.MaximumFileBytes));
        Assert.Contains("[truncated]", File.ReadAllText(log.FilePath));
    }

    private SettingsStore Store() => new(Path.Combine(_directory, "settings.json"), new AppLog(Path.Combine(_directory, "logs")));

    public void Dispose()
    {
        // This fixture creates a unique, test-owned temporary directory only.
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
