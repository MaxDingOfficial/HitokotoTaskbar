using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Native;

/// <summary>Only the small text HWND is interactive; the taskbar is never parented or modified.</summary>
public static class NativeWindow
{
    public static WindowDiagnostic Inspect(nint window)
    {
        _ = NativeMethods.GetWindowRect(window, out var rect);
        return new WindowDiagnostic(window.ToInt64(), NativeMethods.IsWindow(window),
            NativeMethods.IsWindowVisible(window), rect.ToPixelRect(),
            NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64(),
            NativeMethods.GetDpiForWindow(window), NativeMethods.GetForegroundWindow().ToInt64());
    }

    public static IDisposable AttachNoActivate(HwndSource source)
    {
        var styles = NativeMethods.GetWindowLongPtr(source.Handle, NativeMethods.GwlExStyle).ToInt64();
        styles |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        styles &= ~NativeMethods.WsExAppWindow;
        NativeMethods.SetWindowLongPtr(source.Handle, NativeMethods.GwlExStyle, new nint(styles));
        return new NonActivatingHook(source);
    }

    /// <summary>Physical screen pixels. WPF Left/Top in DIPs must not be passed here.</summary>
    public static void PlaceNoActivate(nint window, PixelRect rectangle)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            Hide(window);
            return;
        }
        if (!NativeMethods.SetWindowPos(window, NativeMethods.HwndTopmost,
            rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法放置任务栏一言窗口。");
    }

    public static void Hide(nint window) => NativeMethods.ShowWindow(window, 0 /* SW_HIDE */);

    private sealed class NonActivatingHook : IDisposable
    {
        private HwndSource? _source;
        internal NonActivatingHook(HwndSource source)
        {
            _source = source;
            source.AddHook(Hook);
        }
        private nint Hook(nint window, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == NativeMethods.WmMouseActivate)
            {
                handled = true;
                return NativeMethods.MaNoActivate;
            }
            return 0;
        }
        public void Dispose()
        {
            if (_source is { IsDisposed: false } source) source.RemoveHook(Hook);
            _source = null;
        }
    }
}

public sealed record WindowDiagnostic(long Handle, bool Exists, bool IsVisible, PixelRect Bounds,
    long ExtendedStyle, uint Dpi, long ForegroundHandle);
