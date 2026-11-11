using System.Runtime.InteropServices;
using System.Windows.Automation;
using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Native;

public sealed record MonitorDescriptor(string DeviceName, string DisplayName, bool IsPrimary,
    PixelRect Bounds, PixelRect WorkArea, double Scale);

public sealed record TaskbarSnapshot(MonitorDescriptor Monitor, nint TaskbarHandle,
    PixelRect TaskbarRect, PixelRect SafeRegion, bool IsVisible, bool IsFullscreen, string Description);

/// <summary>
/// Reads Explorer without modifying it. All returned geometry is in physical screen pixels.
/// A single background MTA performs UI Automation. A stalled provider causes a hidden overlay,
/// never an unbounded accumulation of tasks/threads or a blocked WPF dispatcher.
/// </summary>
public sealed class TaskbarProbe : IDisposable
{
    private readonly AutomationWorker _automation = new();
    private readonly object _cacheLock = new();
    private LayoutCache? _cache;
    private int _generation;
    private int _cacheVersion;
    private bool _disposed;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromMilliseconds(1500);

    public static IReadOnlyList<MonitorDescriptor> EnumerateMonitors()
    {
        var result = new List<MonitorDescriptor>();
        var taskbars = FindTaskbars();
        NativeMethods.MonitorEnumProc callback = (nint handle, nint dc, ref NativeMethods.Rect rect, nint state) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>(), DeviceName = string.Empty };
            if (!NativeMethods.GetMonitorInfo(handle, ref info)) return true;
            var taskbar = taskbars.FirstOrDefault(window => NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest) == handle);
            var dpi = taskbar != 0 ? NativeMethods.GetDpiForWindow(taskbar) : 0;
            if (dpi == 0 && NativeMethods.GetDpiForMonitor(handle, 0, out var x, out _) == 0) dpi = x;
            var primary = (info.Flags & 1) != 0;
            result.Add(new MonitorDescriptor(info.DeviceName,
                $"{info.DeviceName} · {info.Monitor.Right - info.Monitor.Left} × {info.Monitor.Bottom - info.Monitor.Top}" + (primary ? "（主显示器）" : string.Empty),
                primary, info.Monitor.ToPixelRect(), info.Work.ToPixelRect(), dpi > 0 ? dpi / 96d : 1d));
            return true;
        };
        NativeMethods.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        return result.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ToList();
    }

    public void Invalidate(bool invalidateInFlight = true)
    {
        if (invalidateInFlight) Interlocked.Increment(ref _generation);
        Interlocked.Increment(ref _cacheVersion);
        lock (_cacheLock) _cache = null;
    }

    public async Task<TaskbarSnapshot?> ProbeAsync(string? monitorDeviceName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var monitors = EnumerateMonitors();
        var monitor = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, monitorDeviceName, StringComparison.OrdinalIgnoreCase))
                      ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        if (monitor is null) return null;
        var taskbar = FindTaskbars().FirstOrDefault(window =>
        {
            var handle = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
            var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>(), DeviceName = string.Empty };
            return NativeMethods.GetMonitorInfo(handle, ref info) && string.Equals(info.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase);
        });
        if (taskbar == 0 || !NativeMethods.GetWindowRect(taskbar, out var nativeRect))
            return Hidden(monitor, taskbar, default, "所选显示器上未找到任务栏；等待 Explorer 或显示器恢复。");

        var rect = nativeRect.ToPixelRect();
        var dpi = NativeMethods.GetDpiForWindow(taskbar);
        if (dpi > 0) monitor = monitor with { Scale = dpi / 96d };
        if (!NativeMethods.IsWindowVisible(taskbar) || NativeMethods.IsCloaked(taskbar))
            return Hidden(monitor, taskbar, rect, "任务栏当前不可见。");
        if (IsAutoHideEnabled(taskbar, monitor.Bounds))
            return Hidden(monitor, taskbar, rect, "任务栏已启用自动隐藏：为避免文字悬浮在桌面边缘，一言暂停显示。");
        // Windows 11's supported taskbar is horizontal and at the bottom. During Explorer
        // recreation/animation it can briefly report stale or partly off-screen geometry.
        if (rect.Height < 12 * monitor.Scale || rect.Width < rect.Height * 3 ||
            rect.Top < monitor.Bounds.Top || rect.Bottom > monitor.Bounds.Bottom + 2 ||
            Math.Abs(rect.Bottom - monitor.Bounds.Bottom) > 2 || rect.Left < monitor.Bounds.Left - 2 || rect.Right > monitor.Bounds.Right + 2)
            return Hidden(monitor, taskbar, rect, "任务栏不在可安全覆盖的底部位置，或正在移动/隐藏。");

        var fullscreen = FullscreenDetector.IsFullscreenOnMonitor(monitor.Bounds);
        if (fullscreen) return Hidden(monitor, taskbar, rect, "此显示器上的前台应用覆盖整个屏幕。", true);

        var generation = Volatile.Read(ref _generation);
        var cacheVersion = Volatile.Read(ref _cacheVersion);
        LayoutScan? scan = null;
        lock (_cacheLock)
        {
            if (_cache is { } cache && cache.Window == taskbar && cache.Bounds == rect &&
                cache.Generation == generation && DateTimeOffset.UtcNow - cache.FetchedAt < CacheLifetime)
                scan = cache.Scan;
        }
        if (scan is null)
        {
            if (!_automation.TryScan(taskbar, rect, monitor.Scale, out var pending))
                return Hidden(monitor, taskbar, rect, "等待 Windows 任务栏辅助功能接口响应。");
            try
            {
                scan = await pending.WaitAsync(ProviderTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return Hidden(monitor, taskbar, rect, "读取任务栏控件超时，暂时隐藏以免遮挡图标。");
            }
            if (generation != Volatile.Read(ref _generation))
                return Hidden(monitor, taskbar, rect, "任务栏布局刚刚改变，等待重新定位。");
            lock (_cacheLock)
            {
                // A content event while UIA was running must not resurrect the invalidated
                // cache. The next low-frequency check will rescan instead of reusing it.
                if (cacheVersion == Volatile.Read(ref _cacheVersion))
                    _cache = new LayoutCache(taskbar, rect, generation, DateTimeOffset.UtcNow, scan);
            }
        }

        if (!scan.IsReliable || scan.SafeRegion.Width < Math.Ceiling(48 * monitor.Scale))
            return Hidden(monitor, taskbar, rect, scan.Description);

        // Refresh cheap visibility/fullscreen signals after the asynchronous provider call.
        if (!NativeMethods.IsWindow(taskbar) || !NativeMethods.IsWindowVisible(taskbar) ||
            !NativeMethods.GetWindowRect(taskbar, out var current) || current.ToPixelRect() != rect)
            return Hidden(monitor, taskbar, rect, "任务栏在读取期间发生改变，等待重新定位。");
        if (IsAutoHideEnabled(taskbar, monitor.Bounds))
            return Hidden(monitor, taskbar, rect, "任务栏已启用自动隐藏，一言暂停显示。");
        if (FullscreenDetector.IsFullscreenOnMonitor(monitor.Bounds))
            return Hidden(monitor, taskbar, rect, "此显示器上的前台应用覆盖整个屏幕。", true);

        return new TaskbarSnapshot(monitor, taskbar, rect, scan.SafeRegion, true, false, scan.Description);
    }

    private static TaskbarSnapshot Hidden(MonitorDescriptor monitor, nint taskbar, PixelRect rect, string description, bool fullscreen = false) =>
        new(monitor, taskbar, rect, default, false, fullscreen, description);

    private static bool IsAutoHideEnabled(nint taskbar, PixelRect monitor)
    {
        var data = new NativeMethods.AppBarData { Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(), Window = taskbar };
        if ((NativeMethods.SHAppBarMessage(4 /* ABM_GETSTATE */, ref data) & 1) != 0) return true;
        data.Edge = 3; // ABE_BOTTOM
        data.Bounds = new NativeMethods.Rect { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom };
        return NativeMethods.SHAppBarMessage(0xB /* ABM_GETAUTOHIDEBAREX */, ref data) != 0;
    }

    private static List<nint> FindTaskbars()
    {
        var result = new List<nint>();
        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != 0) result.Add(primary);
        NativeMethods.EnumWindowsProc callback = (window, parameter) =>
        {
            if (NativeMethods.ClassName(window) == "Shell_SecondaryTrayWnd") result.Add(window);
            return true;
        };
        NativeMethods.EnumWindows(callback, 0);
        GC.KeepAlive(callback);
        return result;
    }

    private static LayoutScan ScanLayout(nint taskbar, PixelRect bar, double scale)
    {
        try
        {
            // UIA rectangles are physical screen coordinates, even when WPF uses DIPs.
            // Cache every required property in one provider request; avoid dozens of RPCs.
            var request = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
            request.Add(AutomationElement.BoundingRectangleProperty);
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ClassNameProperty);
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.IsOffscreenProperty);
            request.Add(AutomationElement.IsKeyboardFocusableProperty);
            var root = AutomationElement.FromHandle(taskbar);
            AutomationElementCollection elements;
            using (request.Activate())
            {
                elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            }
            var occupied = new List<PixelRect>();
            int? startLeft = null;
            foreach (AutomationElement element in elements)
            {
                var info = element.Cached;
                var bounds = info.BoundingRectangle;
                if (info.IsOffscreen || bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Right) ||
                    bounds.Width <= 0 || bounds.Height <= 0 || bounds.Bottom <= bar.Top || bounds.Top >= bar.Bottom ||
                    bounds.Right <= bar.Left || bounds.Left >= bar.Right) continue;
                var id = info.AutomationId ?? string.Empty;
                var className = info.ClassName ?? string.Empty;
                var type = info.ControlType;
                var isStart = id.Equals("StartButton", StringComparison.OrdinalIgnoreCase) ||
                              className.Equals("Start", StringComparison.OrdinalIgnoreCase) ||
                              (type == ControlType.Button && info.Name is "开始" or "開始" or "Start");
                var actionable = isStart || info.IsKeyboardFocusable || type == ControlType.Button ||
                    type == ControlType.CheckBox || type == ControlType.RadioButton || type == ControlType.Hyperlink ||
                    type == ControlType.Edit || type == ControlType.ComboBox || type == ControlType.ListItem ||
                    type == ControlType.MenuItem || type == ControlType.TabItem || type == ControlType.SplitButton;
                if (!actionable) continue;
                var left = Math.Max(bar.Left, (int)Math.Floor(bounds.Left));
                var right = Math.Min(bar.Right, (int)Math.Ceiling(bounds.Right));
                if (right <= left) continue;
                occupied.Add(new PixelRect(left, bar.Top, right, bar.Bottom));
                if (isStart) startLeft = startLeft.HasValue ? Math.Min(startLeft.Value, left) : left;
            }
            if (!startLeft.HasValue)
                return new LayoutScan(false, default, "无法可靠识别开始按钮的位置；为避免覆盖任务栏控件，暂停显示。");

            // Only use a gap BEFORE Start. This also handles a Widgets button at the left:
            // the chosen gap starts after its right edge. Left-aligned taskbars have no such
            // gap and intentionally hide; no guessed percentage or fixed 4K coordinate.
            var best = TaskbarGapRules.FindBeforeStart(bar, startLeft.Value, occupied, scale);
            if (best.IsEmpty)
                return new LayoutScan(false, default, "开始按钮左侧没有足够空白区域（一言已隐藏）。");
            return new LayoutScan(true, best, $"已识别 {occupied.Count} 个交互控件；可用物理像素区间 {best.Left}–{best.Right}，DPI {scale * 96:0}。");
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
        {
            return new LayoutScan(false, default, $"任务栏辅助功能信息暂不可用：{exception.GetType().Name}。");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _automation.Dispose();
        lock (_cacheLock) _cache = null;
    }

    private sealed record LayoutCache(nint Window, PixelRect Bounds, int Generation, DateTimeOffset FetchedAt, LayoutScan Scan);
    private sealed record LayoutScan(bool IsReliable, PixelRect SafeRegion, string Description);

    private sealed class AutomationWorker : IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<ScanRequest> _requests = new(1);
        private readonly Thread _thread;
        private int _busy;
        private bool _disposed;
        internal AutomationWorker()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Hitokoto taskbar UIA" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }
        internal bool TryScan(nint window, PixelRect bounds, double scale, out Task<LayoutScan> result)
        {
            result = Task.FromResult(new LayoutScan(false, default, "任务栏读取已停止。"));
            if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
            var completion = new TaskCompletionSource<LayoutScan>(TaskCreationOptions.RunContinuationsAsynchronously);
            result = completion.Task;
            try
            {
                if (_requests.TryAdd(new ScanRequest(window, bounds, scale, completion))) return true;
            }
            catch (InvalidOperationException) { }
            Interlocked.Exchange(ref _busy, 0);
            return false;
        }
        private void Run()
        {
            try
            {
                foreach (var request in _requests.GetConsumingEnumerable())
                {
                    LayoutScan result;
                    try
                    {
                        result = ScanLayout(request.Window, request.Bounds, request.Scale);
                    }
                    catch (Exception exception)
                    {
                        result = new LayoutScan(false, default,
                            $"任务栏读取失败：{exception.GetType().Name}。");
                    }
                    // Make the worker available before waking a caller that may immediately
                    // probe another monitor, avoiding a spurious busy/hidden result.
                    Interlocked.Exchange(ref _busy, 0);
                    request.Completion.TrySetResult(result);
                }
            }
            finally { _requests.Dispose(); }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _requests.CompleteAdding();
            // A broken out-of-process provider cannot be safely aborted. The single thread
            // is background-only and never keeps the process alive; no replacements spawn.
            _thread.Join(100);
        }
        private sealed record ScanRequest(nint Window, PixelRect Bounds, double Scale, TaskCompletionSource<LayoutScan> Completion);
    }
}
