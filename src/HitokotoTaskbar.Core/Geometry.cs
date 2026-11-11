namespace HitokotoTaskbar.Core;

/// <summary>Screen-space physical pixels, including negative coordinates on monitors to the left/top.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public bool IsEmpty => Width == 0 || Height == 0;
}

public static class FullscreenRules
{
    /// <summary>Work-area maximization is not fullscreen. Compare against the complete monitor rectangle.</summary>
    public static bool IsFullscreen(PixelRect window, PixelRect monitor, int tolerance = 2)
    {
        tolerance = Math.Clamp(tolerance, 0, 16);
        return !window.IsEmpty && !monitor.IsEmpty &&
            window.Left <= monitor.Left + tolerance && window.Top <= monitor.Top + tolerance &&
            window.Right >= monitor.Right - tolerance && window.Bottom >= monitor.Bottom - tolerance;
    }
}

public static class LayoutRules
{
    /// <summary>
    /// Computes a free horizontal corridor. safeStartPx/safeEndPx are absolute physical coordinates
    /// discovered from actual shell controls. The vertical offset is applied by PlaceText after measurement.
    /// </summary>
    public static bool TryPlace(PixelRect taskbar, double dpiScale, double leftDip, double maxWidthDip,
        double verticalDip, int safeStartPx, int safeEndPx, out PixelRect area)
    {
        area = default;
        if (taskbar.IsEmpty || !double.IsFinite(dpiScale) || dpiScale <= 0 || dpiScale > 10 ||
            !double.IsFinite(leftDip) || !double.IsFinite(maxWidthDip) || !double.IsFinite(verticalDip) || maxWidthDip <= 0)
            return false;
        var left = Math.Max(taskbar.Left + Round(Math.Max(0, leftDip) * dpiScale), Math.Max(taskbar.Left, safeStartPx));
        var right = Math.Min(Math.Min(taskbar.Right, safeEndPx), left + Round(Math.Min(10000, maxWidthDip) * dpiScale));
        if (right - left < Math.Ceiling(24 * dpiScale)) return false;
        area = new(left, taskbar.Top, right, taskbar.Bottom);
        return true;
    }

    /// <summary>Converts WPF's measured DIPs once, then clamps both axes inside the free physical rectangle.</summary>
    public static PixelRect PlaceText(PixelRect corridor, double dpiScale, double desiredWidthDip, double desiredHeightDip, double verticalDip = 0)
    {
        if (corridor.IsEmpty || !double.IsFinite(dpiScale) || dpiScale <= 0 ||
            !double.IsFinite(desiredWidthDip) || !double.IsFinite(desiredHeightDip) || !double.IsFinite(verticalDip))
            return default;
        var width = Math.Clamp(Round(desiredWidthDip * dpiScale), 1, corridor.Width);
        var height = Math.Clamp(Round(desiredHeightDip * dpiScale), 1, corridor.Height);
        var top = Math.Clamp(corridor.Top + (corridor.Height - height) / 2 + Round(verticalDip * dpiScale), corridor.Top, corridor.Bottom - height);
        return new(corridor.Left, top, corridor.Left + width, top + height);
    }

    private static int Round(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue / 2d, int.MaxValue / 2d);
}
