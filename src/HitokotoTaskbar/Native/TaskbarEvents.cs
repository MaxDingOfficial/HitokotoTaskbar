using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HitokotoTaskbar.Native;

[Flags]
public enum TaskbarChangeKind
{
    Foreground = 1,
    // A provider event might be an icon move or a harmless clock/tooltip update. Reprobe
    // without unconditionally blinking the quote off on every such notification.
    Contents = 2,
    Geometry = 4,
    Visibility = 8,
    Environment = 16
}

/// <summary>Read-only notifications. Caller should debounce and retain a slow recovery timer.</summary>
public sealed class TaskbarEvents : IDisposable
{
    private readonly HwndSource _source;
    private readonly Action<TaskbarChangeKind> _changed;
    private readonly NativeMethods.WinEventProc _callback;
    private readonly List<nint> _hooks = [];
    private readonly HashSet<nint> _knownTaskbars = [];
    private readonly Dictionary<nint, HitokotoTaskbar.Core.PixelRect> _knownBounds = [];
    private readonly uint _taskbarCreated;
    private GCHandle _callbackLifetime;
    private bool _disposed;

    public TaskbarEvents(HwndSource source, Action changed) : this(source, _ => changed()) { }

    public TaskbarEvents(HwndSource source, Action<TaskbarChangeKind> changed)
    {
        _source = source;
        _changed = changed;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _callback = OnWinEvent;
        _callbackLifetime = GCHandle.Alloc(_callback);
        _source.AddHook(WindowHook);
        AddHook(3, 3); // EVENT_SYSTEM_FOREGROUND
        AddHook(0x000B, 0x000B); // EVENT_SYSTEM_MOVESIZEEND
        AddHook(0x0016, 0x0017); // minimize start/end
        AddHook(0x0020, 0x0020); // desktop switch
        AddHook(0x8000, 0x8003); // create, destroy, show, hide
        AddHook(0x800B, 0x800B); // location change
        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != 0) _knownTaskbars.Add(primary);
    }

    private void AddHook(uint first, uint last)
    {
        var hook = NativeMethods.SetWinEventHook(first, last, 0, _callback, 0, 0,
            0x0000 /* WINEVENT_OUTOFCONTEXT */ | 0x0002 /* WINEVENT_SKIPOWNPROCESS */);
        if (hook != 0) _hooks.Add(hook);
    }

    private void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (_disposed) return;
        try
        {
            if (eventType < 0x8000)
            {
                _changed(eventType == 0x20 ? TaskbarChangeKind.Environment : TaskbarChangeKind.Foreground);
                return;
            }
            if (window == 0) return;
            var root = NativeMethods.GetAncestor(window, 2 /* GA_ROOT */);
            var taskbar = NativeMethods.IsTaskbar(root) ? root : NativeMethods.IsTaskbar(window) ? window : 0;
            if (taskbar != 0)
            {
                _knownTaskbars.Add(taskbar);
                var boundsChanged = NativeMethods.GetWindowRect(taskbar, out var rect) &&
                    (!_knownBounds.TryGetValue(taskbar, out var previous) || previous != rect.ToPixelRect());
                if (boundsChanged) _knownBounds[taskbar] = rect.ToPixelRect();
                _changed(boundsChanged ? TaskbarChangeKind.Geometry :
                    (eventType is 0x8002 or 0x8003 && objectId == 0 && window == taskbar)
                        ? TaskbarChangeKind.Visibility : TaskbarChangeKind.Contents);
            }
            else if (_knownTaskbars.Remove(window))
            {
                _knownBounds.Remove(window);
                _changed(TaskbarChangeKind.Visibility);
            }
            else if (objectId == 0 && window == NativeMethods.GetForegroundWindow()) _changed(TaskbarChangeKind.Foreground);
        }
        catch { /* Exceptions must not escape a native event callback. Slow polling recovers. */ }
    }

    private nint WindowHook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (!_disposed && ((uint)message == _taskbarCreated || message is
            0x007E /* WM_DISPLAYCHANGE */ or 0x001A /* WM_SETTINGCHANGE */ or
            0x02E0 /* WM_DPICHANGED */ or 0x0218 /* WM_POWERBROADCAST */ or
            0x031E /* WM_DWMCOMPOSITIONCHANGED */))
        {
            try { _changed(TaskbarChangeKind.Environment); } catch { }
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_source.IsDisposed) _source.RemoveHook(WindowHook);
        foreach (var hook in _hooks) NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
        if (_callbackLifetime.IsAllocated) _callbackLifetime.Free();
        _knownTaskbars.Clear();
        _knownBounds.Clear();
    }
}
