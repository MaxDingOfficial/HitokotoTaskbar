using System.Drawing;
using System.Drawing.Imaging;
using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Services;

/// <summary>Used only by the explicitly selected isolated smoke test; never captures in normal operation.</summary>
internal static class RuntimeDiagnostics
{
    public static void SaveTaskbarImage(string path, PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 32000 || bounds.Height > 1000) return;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
        bitmap.Save(path, ImageFormat.Png);
    }
}
