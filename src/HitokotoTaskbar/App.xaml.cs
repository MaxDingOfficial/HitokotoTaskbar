using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using HitokotoTaskbar.Core;
using HitokotoTaskbar.Native;
using HitokotoTaskbar.Services;
using HitokotoTaskbar.UI;

namespace HitokotoTaskbar;

public partial class App : Application
{
    internal static string VersionName => typeof(App).Assembly.GetName().Version!.ToString(3);
    private AppLog _log = null!;
    private SettingsStore _store = null!;
    private AppSettings _settings = new();
    private HitokotoService _api = null!;
    private GitHubUpdateService _updater = null!;
    private TaskbarProbe _probe = null!;
    private readonly StartupService _startup = new();
    private readonly CancellationTokenSource _stop = new();
    private InstanceService? _instance;
    private TaskbarOverlays? _overlays;
    private HwndSource? _eventHost;
    private TrayService? _tray;
    private TaskbarEvents? _events;
    private SettingsWindow? _settingsWindow;
    private ActionMenuWindow? _menu;
    private DispatcherTimer? _refreshTimer, _placementTimer, _eventDebounce, _updateTimer;
    private Task? _fetchTask, _placementTask, _updateTask;
    private IReadOnlyList<TaskbarSnapshot> _lastSnapshots = [];
    private GitHubRelease? _pendingUpdate;
    private string _placementStatus = "正在检查任务栏空白区域…";
    private string _fetchStatus = "";
    private bool _exiting, _sessionLocked, _smoke;
    private int _layoutVersion;
    private int _successfulFetches;
    private DateTimeOffset? _firstFetchedAt;
    private nint _foregroundAtStartup;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _smoke = e.Args.Contains("--smoke-test");
            var smokePath = GetArgument(e.Args, "--smoke-test");
            var dataRoot = _smoke ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(smokePath!))!, "smoke-state") : null;
            _log = new AppLog(dataRoot is null ? null : Path.Combine(dataRoot, "logs"));
            _store = new SettingsStore(dataRoot is null ? null : Path.Combine(dataRoot, "settings.json"), _log);
            DispatcherUnhandledException += (_, args) =>
            {
                _log.Write("未处理的界面异常，程序将安全退出。", args.Exception);
                args.Handled = true;
                _ = ExitAsync();
            };
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            if (e.Args.Contains("--diagnose"))
            {
                await DiagnoseAsync(GetArgument(e.Args, "--diagnose")!);
                Shutdown(0);
                return;
            }
            if (!_smoke)
            {
                _instance = new InstanceService();
                if (!_instance.IsFirst)
                {
                    bool sent = await InstanceService.SendAsync(e.Args.Contains("--exit") ? "exit" :
                        e.Args.Contains("--refresh") ? "refresh" : "settings");
                    Shutdown(sent ? 0 : 2);
                    return;
                }
                if (e.Args.Contains("--exit")) { Shutdown(0); return; }
                _instance.Listen(command => Dispatcher.BeginInvoke(() => HandleCommand(command)),
                    exception => _log.Write("本地进程通信异常。", exception));
            }
            _settings = _store.Load();
            try { _settings.AutoStart = !_smoke && _startup.IsEnabled(); }
            catch (Exception ex) { _log.Write("无法读取当前用户启动项。", ex); _settings.AutoStart = false; }
            _api = new HitokotoService();
            _updater = new GitHubUpdateService();
            _probe = new TaskbarProbe();
            _foregroundAtStartup = GetForegroundWindow();
            _overlays = new TaskbarOverlays(BeginFetch, ShowMenu);
            _overlays.Apply(_settings, _settings.LastQuote);
            _tray = new TrayService();
            _tray.MenuRequested += ShowMenu;
            _tray.SettingsRequested += ShowSettings;
            _tray.SetVisible(_settings.ShowTrayIcon);
            // A stable, hidden top-level HWND receives Explorer broadcasts even when every
            // overlay is closed after changing monitors. Message-only HWNDs miss broadcasts.
            _eventHost = new HwndSource(new HwndSourceParameters("Hitokoto Taskbar notifications")
            {
                WindowStyle = unchecked((int)0x80000000), // WS_POPUP, no WS_VISIBLE
                ExtendedWindowStyle = 0x08000080, // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                Width = 1, Height = 1
            });
            _events = new TaskbarEvents(_eventHost, kind => StateChanged(kind));
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
                { Interval = TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes) };
            _refreshTimer.Tick += (_, _) => BeginFetch();
            _refreshTimer.Start();
            _placementTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _placementTimer.Tick += (_, _) => BeginPlacement();
            _placementTimer.Start();
            _eventDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            _eventDebounce.Tick += (_, _) => { _eventDebounce.Stop(); BeginPlacement(); };
            if (!_smoke)
            {
                _updateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromDays(1) };
                _updateTimer.Tick += (_, _) => BeginUpdateCheck(false);
                _updateTimer.Start();
                BeginUpdateCheck(false);
            }
            _log.Write("程序启动。" + (_smoke ? "隔离冒烟测试模式。" : ""));
            BeginPlacement();
            BeginFetch();
            if (e.Args.Contains("--settings")) ShowSettings();
            if (_smoke)
            {
                var duration = GetArgument(e.Args, "--smoke-seconds");
                await CompleteSmokeAsync(smokePath!, int.TryParse(duration, out var seconds) ? Math.Clamp(seconds, 18, 600) : 18);
            }
        }
        catch (Exception ex)
        {
            _log?.Write("启动失败。", ex);
            Shutdown(1);
        }
    }

    private static string? GetArgument(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException(option + " 需要一个输出 JSON 路径。");
        return args[index + 1];
    }
    private void HandleCommand(string command)
    {
        if (_exiting) return;
        switch (command)
        {
            case "exit": _ = ExitAsync(); break;
            case "refresh": BeginFetch(); break;
            default: ShowSettings(); break;
        }
    }
    private void BeginFetch()
    {
        if (_exiting || _fetchTask is { IsCompleted: false }) return;
        _fetchTask = FetchAsync();
    }
    private async Task FetchAsync()
    {
        try
        {
            var result = await _api.FetchAsync(_settings.Copy(), _stop.Token);
            if (_exiting) return;
            if (result.Status == FetchStatus.Success && result.Quote is { } quote)
            {
                _settings.LastQuote = quote;
                _successfulFetches++;
                _firstFetchedAt ??= quote.FetchedAt;
                _fetchStatus = "";
                // All settings writes are serialized on the dispatcher, including settings
                // changes, so an older cache snapshot can never overwrite newer preferences.
                try { _store.Save(_settings.Copy()); }
                catch (Exception ex) { _log.Write("语句已获取，但缓存写入失败。", ex); _fetchStatus = "缓存未能保存。"; }
                _tray?.SetText("Hitokoto Taskbar · " + quote.Text);
            }
            else
            {
                _fetchStatus = result.Status == FetchStatus.Throttled ? "刷新过于频繁，请稍后再试。" : "暂时无法获取新语句，保留上次内容。";
                if (result.Status == FetchStatus.Failed) _log.Write("一言获取失败：" + result.Error);
            }
            _overlays?.Apply(_settings, _settings.LastQuote, _fetchStatus);
            BeginPlacement();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Write("刷新语句异常，保留当前语句。", ex); }
    }
    private void BeginPlacement()
    {
        if (_exiting || _placementTask is { IsCompleted: false }) return;
        _placementTask = UpdatePlacementAsync();
    }
    private async Task UpdatePlacementAsync()
    {
        try
        {
            if (_sessionLocked) { _overlays?.ConcealAll(); return; }
            int version = _layoutVersion;
            var snapshots = await _probe.ProbeDisplaysAsync(_settings.MonitorDeviceName, _settings.ShowOnAllDisplays, _stop.Token);
            if (_exiting || version != _layoutVersion) return;
            _lastSnapshots = snapshots;
            var status = snapshots.Count switch
            {
                0 => "没有可用显示器，等待恢复。",
                1 => snapshots[0].Description,
                _ => string.Join("\n", snapshots.Select(snapshot => $"{snapshot.Monitor.DeviceName}：{snapshot.Description}"))
            };
            if (status != _placementStatus) { _placementStatus = status; _log.Write(status); }
            _settingsWindow?.UpdateStatus(status);
            if (_sessionLocked) { _overlays?.ConcealAll(); _menu?.Close(); return; }
            _overlays?.Place(snapshots);
            TryOpenPendingUpdate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _overlays?.ConcealAll(); _log.Write("任务栏定位失败，已隐藏文字。", ex); }
    }
    private void StateChanged() => StateChanged(TaskbarChangeKind.Environment);
    private void StateChanged(TaskbarChangeKind kind)
    {
        if (_exiting || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_exiting) return;
            if (kind == TaskbarChangeKind.Contents)
            {
                _probe.Invalidate(false);
            }
            else if (kind == TaskbarChangeKind.Foreground)
            {
                foreach (var snapshot in _lastSnapshots)
                    if (FullscreenDetector.IsFullscreenOnMonitor(snapshot.Monitor.Bounds) &&
                        _overlays?.Conceal(snapshot.Monitor.DeviceName) == true) _menu?.Close();
            }
            else
            {
                _layoutVersion++;
                _probe.Invalidate();
                _overlays?.ConcealAll();
            }
            // Do not restart an already pending timer: noisy shell providers must not
            // starve the reposition forever or cause a blink on harmless clock events.
            if (_eventDebounce is { IsEnabled: false }) _eventDebounce.Start();
        });
    }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _sessionLocked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff;
            StateChanged();
        });
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) StateChanged();
    }
    private IEnumerable<MenuAction> MenuActions()
    {
        yield return new("下一句", BeginFetch);
        yield return new("复制当前语句", () => _ = CopyQuoteAsync());
        yield return new("打开一言官网", OpenWebsite);
        yield return new("设置", ShowSettings);
        yield return new("检查更新", () => BeginUpdateCheck(true));
        yield return new("开机自动启动", ToggleStartup, _settings.AutoStart);
        yield return new("退出程序", () => _ = ExitAsync());
    }
    private void ShowMenu()
    {
        if (_exiting) return;
        try
        {
            _menu?.Close();
            _menu = new ActionMenuWindow(MenuActions());
            _menu.Closed += (_, _) => _menu = null;
            _menu.ShowNearCursor();
        }
        catch (Exception ex) { _log.Write("显示菜单失败。", ex); }
    }
    private async Task CopyQuoteAsync()
    {
        if (_settings.LastQuote is not { } quote) return;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { Clipboard.SetText(quote.Text); return; }
            catch (COMException)
            {
                try { await Task.Delay(100, _stop.Token); } catch (OperationCanceledException) { return; }
            }
        }
        _tray?.Notify("剪贴板正被占用，请稍后重试。");
    }
    private void OpenWebsite()
    {
        try { Process.Start(new ProcessStartInfo("https://hitokoto.cn/") { UseShellExecute = true }); }
        catch (Exception ex) { _log.Write("无法打开一言官网。", ex); _tray?.Notify("无法打开默认浏览器。"); }
    }
    private void BeginUpdateCheck(bool manual)
    {
        if (_exiting || _smoke || (!manual && !_settings.AutomaticallyCheckForUpdates) || _updateTask is { IsCompleted: false }) return;
        _updateTask = CheckForUpdateAsync(manual);
    }
    private async Task CheckForUpdateAsync(bool manual)
    {
        try
        {
            var result = await _updater.CheckAsync(Version.Parse(VersionName), _stop.Token);
            if (_exiting) return;
            if (result.Release is { } release)
            {
                if (manual) OpenReleasePage(release, true);
                else if (_settings.AutomaticallyCheckForUpdates && release.ShouldOpenAutomatically(_settings.LastOpenedUpdateVersion))
                {
                    _pendingUpdate = release;
                    TryOpenPendingUpdate();
                }
                return;
            }
            if (result.Status == UpdateStatus.Unavailable) _log.Write("检查更新失败：" + result.Error);
            if (manual) ShowUpdateMessage(result.Status == UpdateStatus.Current
                ? $"当前版本 v{VersionName}，未发现更新。"
                : "暂时无法连接 GitHub 检查更新，请稍后再试。");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log.Write("检查更新失败。", ex);
            if (manual && !_exiting) ShowUpdateMessage("暂时无法检查更新，请稍后再试。");
        }
    }
    private void TryOpenPendingUpdate()
    {
        if (_pendingUpdate is not { } release || !_settings.AutomaticallyCheckForUpdates || _sessionLocked || _exiting) return;
        if (!release.ShouldOpenAutomatically(_settings.LastOpenedUpdateVersion)) { _pendingUpdate = null; return; }
        if (TaskbarProbe.EnumerateMonitors().Any(monitor => FullscreenDetector.IsFullscreenOnMonitor(monitor.Bounds))) return;
        _pendingUpdate = null;
        OpenReleasePage(release, false);
    }
    private void OpenReleasePage(GitHubRelease release, bool manual)
    {
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(release.DownloadPage.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Write("无法打开 GitHub 下载页。", ex);
            if (manual) ShowUpdateMessage("无法打开默认浏览器。请访问 GitHub Releases 下载新版。");
            return;
        }
        if (!release.ShouldOpenAutomatically(_settings.LastOpenedUpdateVersion)) return;
        _settings.LastOpenedUpdateVersion = release.Version.ToString(3);
        try { _store.Save(_settings.Copy()); }
        catch (Exception ex) { _log.Write("无法保存已打开的更新版本。", ex); }
    }
    private static void ShowUpdateMessage(string text) =>
        MessageBox.Show(text, "Hitokoto Taskbar · 检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
    private void ShowSettings()
    {
        if (_exiting) return;
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Show();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _placementStatus, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
    private string? ApplySettings(AppSettings updated)
    {
        bool startupChanged = updated.AutoStart != _settings.AutoStart;
        bool previousStartup = _settings.AutoStart;
        bool enabledUpdateCheck = updated.AutomaticallyCheckForUpdates && !_settings.AutomaticallyCheckForUpdates;
        try
        {
            updated.LastQuote = _settings.LastQuote;
            updated.LastOpenedUpdateVersion = _settings.LastOpenedUpdateVersion;
            updated = updated.Validated();
            if (startupChanged && !_smoke) _startup.SetEnabled(updated.AutoStart);
            _store.Save(updated);
            _settings = updated;
            _refreshTimer!.Stop();
            _refreshTimer.Interval = TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes);
            _refreshTimer.Start();
            _tray?.SetVisible(_settings.ShowTrayIcon);
            _overlays?.Apply(_settings, _settings.LastQuote);
            if (!_settings.AutomaticallyCheckForUpdates) _pendingUpdate = null;
            if (enabledUpdateCheck) BeginUpdateCheck(false);
            StateChanged();
            return null;
        }
        catch (Exception ex)
        {
            if (startupChanged && !_smoke)
                try { _startup.SetEnabled(previousStartup); } catch (Exception rollback) { _log.Write("恢复启动项失败。", rollback); }
            _log.Write("保存设置失败。", ex);
            return "保存失败：" + ex.Message;
        }
    }
    private void ToggleStartup()
    {
        var updated = _settings.Copy();
        updated.AutoStart = !updated.AutoStart;
        var error = ApplySettings(updated);
        if (error is not null) _tray?.Notify(error);
    }
    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _refreshTimer?.Stop(); _placementTimer?.Stop(); _eventDebounce?.Stop(); _updateTimer?.Stop();
        _events?.Dispose(); _events = null;
        _stop.Cancel();
        _overlays?.ConcealAll();
        _menu?.Close(); _settingsWindow?.Close();
        try
        {
            var work = new[] { _fetchTask, _placementTask, _updateTask }.Where(task => task is not null).Cast<Task>();
            await Task.WhenAll(work);
            if (_instance is not null) await _instance.StopAsync();
        }
        catch (Exception ex) { _log.Write("退出时的后台任务异常。", ex); }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _stop.Cancel();
        _refreshTimer?.Stop(); _placementTimer?.Stop(); _eventDebounce?.Stop(); _updateTimer?.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _events?.Dispose(); _eventHost?.Dispose(); _overlays?.Dispose();
        _tray?.Dispose(); _api?.Dispose(); _probe?.Dispose(); _updater?.Dispose();
        _instance?.Dispose();
        _stop.Dispose();
        _log?.Write("程序退出，已取消后台工作并释放托盘和系统钩子。");
        base.OnExit(e);
    }
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    { _log.Write("后台任务异常。", e.Exception); e.SetObserved(); }

    private async Task DiagnoseAsync(string path)
    {
        using var probe = new TaskbarProbe();
        var samples = (await probe.ProbeDisplaysAsync(null, true)).Select(SnapshotForJson);
        await WriteJsonAsync(path, new { version = VersionName, at = DateTimeOffset.Now, os = Environment.OSVersion.ToString(), samples });
    }
    private async Task CompleteSmokeAsync(string path, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds), _stop.Token);
        var updateCheck = await _updater.CheckAsync(Version.Parse(VersionName), _stop.Token);
        var overlays = _overlays!.Inspect();
        var snapshots = _lastSnapshots;
        for (var index = 0; index < snapshots.Count; index++)
            if (overlays.TryGetValue(snapshots[index].Monitor.DeviceName, out var overlay) && overlay.IsVisible)
                RuntimeDiagnostics.SaveTaskbarImage(Path.ChangeExtension(path, index == 0 ? ".png" : $".display-{index + 1}.png"), snapshots[index].TaskbarRect);
        var diagnostic = overlays.Values.FirstOrDefault();
        await WriteJsonAsync(path, new
        {
            version = VersionName, at = DateTimeOffset.Now, mode = "isolated-smoke-test",
            foregroundAtStartup = _foregroundAtStartup.ToInt64(), foregroundAtEnd = GetForegroundWindow().ToInt64(),
            overlayVisible = overlays.Values.Any(overlay => overlay.IsVisible), overlayHandle = diagnostic?.Handle,
            overlayDiagnostic = diagnostic,
            overlays, snapshots = snapshots.Select(SnapshotForJson),
            updateCheck = new { status = updateCheck.Status.ToString(), latestVersion = updateCheck.LatestVersion?.ToString(3), updateCheck.Error },
            quote = _settings.LastQuote, fetchStatus = _fetchStatus, snapshot = SnapshotForJson(snapshots.FirstOrDefault()),
            successfulFetches = _successfulFetches, firstFetchedAt = _firstFetchedAt,
            elapsedSeconds = seconds, cpuSeconds = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds,
            workingSetBytes = Environment.WorkingSet,
            settingsFile = _store.FilePath
        });
        await ExitAsync();
    }
    private static object SnapshotForJson(TaskbarSnapshot? s) => s is null ? new { available = false } : new
    {
        available = true, monitor = s.Monitor, taskbarHandle = s.TaskbarHandle.ToInt64(), taskbarRect = s.TaskbarRect,
        safeRegion = s.SafeRegion, isVisible = s.IsVisible, isFullscreen = s.IsFullscreen, description = s.Description
    };
    private static async Task WriteJsonAsync(string path, object value)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
