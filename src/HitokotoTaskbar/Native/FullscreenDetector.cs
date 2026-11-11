using System.Runtime.InteropServices;
using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Native;

public static class FullscreenDetector
{
    public static bool IsFullscreenOnMonitor(PixelRect monitor)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || !NativeMethods.IsWindowVisible(foreground) ||
            NativeMethods.IsIconic(foreground) || NativeMethods.IsCloaked(foreground)) return false;
        _ = NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == (uint)Environment.ProcessId) return false;
        // The shell desktop legitimately spans a screen and is not a video/game.
        if (NativeMethods.ClassName(foreground) is "Progman" or "WorkerW" or
            "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        // Extended frame bounds exclude the invisible resize border of a maximized window.
        // Comparing against the monitor, not its work area, distinguishes normal maximization.
        if (NativeMethods.DwmGetWindowAttribute(foreground, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */,
                out var bounds, (uint)Marshal.SizeOf<NativeMethods.Rect>()) != 0 &&
            !NativeMethods.GetWindowRect(foreground, out bounds)) return false;

        return FullscreenRules.IsFullscreen(bounds.ToPixelRect(), monitor, 2);
    }
}
