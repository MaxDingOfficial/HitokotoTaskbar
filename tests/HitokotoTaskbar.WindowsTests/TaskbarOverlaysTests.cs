using System;
using System.Threading.Tasks;
using HitokotoTaskbar.Core;
using HitokotoTaskbar.Native;
using HitokotoTaskbar.UI;
using Xunit;

namespace HitokotoTaskbar.WindowsTests;

public sealed class TaskbarOverlaysTests
{
    [Fact]
    public Task FullscreenAndDisconnectOnlyAffectTheMatchingOverlay() => StaTest.RunAsync(_ =>
    {
        var first = Snapshot("first", 0, 1);
        var second = Snapshot("second", 3840, 1.5);
        using var overlays = new TaskbarOverlays(() => { }, () => { });
        overlays.Apply(new AppSettings(), new HitokotoQuote { Text = "双屏一言" });
        overlays.Place([first, second], _ => false);
        var original = overlays.Inspect();
        Assert.True(original["first"].IsVisible);
        Assert.True(original["second"].IsVisible);
        Assert.NotEqual(original["first"].Handle, original["second"].Handle);
        Assert.True(original["first"].Bounds.Right <= first.SafeRegion.Right);
        Assert.True(original["second"].Bounds.Left >= second.SafeRegion.Left);

        // A previously safe snapshot must also hide if the corresponding screen goes
        // fullscreen just before placement, without affecting the other screen.
        overlays.Place([first, second], bounds => bounds == second.Monitor.Bounds);
        Assert.True(overlays.Inspect()["first"].IsVisible);
        Assert.False(overlays.Inspect()["second"].IsVisible);
        overlays.Place([first, second], _ => false);
        Assert.True(overlays.Inspect()["second"].IsVisible);

        overlays.Place([first], _ => false);
        Assert.Equal(original["first"].Handle, Assert.Single(overlays.Inspect()).Value.Handle);
        Assert.False(NativeWindow.Inspect((nint)original["second"].Handle).Exists);
    });

    private static TaskbarSnapshot Snapshot(string device, int left, double scale)
    {
        var bar = new PixelRect(left, 2088, left + 3840, 2160);
        var monitor = new MonitorDescriptor(device, device, left == 0,
            new PixelRect(left, 0, left + 3840, 2160), new PixelRect(left, 0, left + 3840, bar.Top), scale);
        return new TaskbarSnapshot(monitor, 0, bar, new PixelRect(left, bar.Top, left + 1200, bar.Bottom), true, false, "fixture");
    }
}
