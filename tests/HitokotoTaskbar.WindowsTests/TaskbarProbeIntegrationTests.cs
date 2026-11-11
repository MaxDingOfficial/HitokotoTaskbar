using System;
using System.Linq;
using System.Threading.Tasks;
using HitokotoTaskbar.Native;
using Xunit;

namespace HitokotoTaskbar.WindowsTests;

public sealed class TaskbarProbeIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public Task RealTaskbarsReturnPhysicalSafeRegionsWithoutChangingDesktopState() => StaTest.RunAsync(async scope =>
    {
        Assert.SkipUnless(Environment.UserInteractive, "An interactive Windows session is required.");
        var monitors = TaskbarProbe.EnumerateMonitors();
        Assert.SkipWhen(monitors.Count == 0, "No monitors are connected.");
        Assert.Single(monitors, m => m.IsPrimary);
        using var probe = new TaskbarProbe();
        var snapshots = await probe.ProbeDisplaysAsync(null, true);
        Assert.Equal(monitors.Select(monitor => monitor.DeviceName), snapshots.Select(snapshot => snapshot.Monitor.DeviceName));
        var visible = 0;
        foreach (var snapshot in snapshots)
        {
            var monitor = snapshot.Monitor;
            Assert.InRange(snapshot.Monitor.Scale, 0.5, 8);
            Assert.False(string.IsNullOrWhiteSpace(snapshot.Description));
            output.WriteLine($"{monitor.DeviceName}: {monitor.Bounds.Width}x{monitor.Bounds.Height}, scale={monitor.Scale}; {snapshot.Description}");
            if (!snapshot.IsVisible) continue; // Auto-hide, missing secondary bar, or unsupported shell are safe states.
            visible++;
            Assert.False(snapshot.IsFullscreen);
            Assert.NotEqual(nint.Zero, snapshot.TaskbarHandle);
            Assert.False(snapshot.SafeRegion.IsEmpty);
            Assert.True(snapshot.SafeRegion.Left >= snapshot.TaskbarRect.Left);
            Assert.True(snapshot.SafeRegion.Right <= snapshot.TaskbarRect.Right);
            Assert.Equal(snapshot.TaskbarRect.Top, snapshot.SafeRegion.Top);
            Assert.Equal(snapshot.TaskbarRect.Bottom, snapshot.SafeRegion.Bottom);
        }
        var single = Assert.Single(await probe.ProbeDisplaysAsync(monitors[^1].DeviceName, false));
        Assert.Equal(monitors[^1].DeviceName, single.Monitor.DeviceName);
        Assert.SkipWhen(visible == 0, "No supported visible taskbar: geometry assertions require a regular desktop session.");
    });
}
