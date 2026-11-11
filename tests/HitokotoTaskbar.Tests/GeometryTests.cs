using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void MaximizedWorkAreaDoesNotCountAsFullscreen()
    {
        var monitor = new PixelRect(0, 0, 3840, 2160);
        Assert.False(FullscreenRules.IsFullscreen(new(0, 0, 3840, 2088), monitor));
        Assert.False(FullscreenRules.IsFullscreen(new(-8, -8, 3848, 2096), monitor));
        Assert.True(FullscreenRules.IsFullscreen(monitor, monitor));
    }

    [Fact]
    public void FullscreenSupportsSecondaryMonitorWithNegativeOrigin()
    {
        var secondary = new PixelRect(-3840, -300, 0, 1860);
        Assert.True(FullscreenRules.IsFullscreen(secondary, secondary));
        Assert.False(FullscreenRules.IsFullscreen(new(0, 0, 3840, 2160), secondary));
    }

    [Theory]
    [InlineData(1, 24, 500)]
    [InlineData(1.5, 36, 750)]
    [InlineData(2, 48, 1000)]
    [InlineData(2.5, 60, 1250)]
    public void DipOffsetsAndWidthsAreConvertedExactlyOnce(double scale, int left, int width)
    {
        Assert.True(LayoutRules.TryPlace(new(0, 2088, 3840, 2160), scale, 24, 500, 0, 0, 1900, out var area));
        Assert.Equal(left, area.Left);
        Assert.Equal(width, area.Width);
        Assert.Equal(2088, area.Top);
        Assert.Equal(2160, area.Bottom);
    }

    [Fact]
    public void WidgetAndCenteredControlsConstrainAvailableWidth()
    {
        Assert.True(LayoutRules.TryPlace(new(0, 2088, 3840, 2160), 1.5, 24, 500, 0, 160, 550, out var area));
        Assert.Equal(new PixelRect(160, 2088, 550, 2160), area);
        Assert.False(LayoutRules.TryPlace(new(0, 2088, 3840, 2160), 1.5, 24, 500, 0, 160, 180, out _));
    }

    [Fact]
    public void MissingOrOverlappingSafeAreaHidesOverlay()
    {
        Assert.False(LayoutRules.TryPlace(new(0, 2088, 3840, 2160), 1.5, 24, 500, 0, 800, 700, out _));
        Assert.False(LayoutRules.TryPlace(default, 1.5, 24, 500, 0, 0, 0, out _));
        Assert.False(LayoutRules.TryPlace(new(0, 2088, 3840, 2160), double.NaN, 24, 500, 0, 0, 1000, out _));
    }

    [Fact]
    public void SecondaryMonitorLayoutPreservesNegativePhysicalCoordinates()
    {
        Assert.True(LayoutRules.TryPlace(new(-3840, 1788, 0, 1860), 2, 24, 500, 0, -3840, -2000, out var area));
        Assert.Equal(new PixelRect(-3792, 1788, -2792, 1860), area);
    }

    [Fact]
    public void MeasuredTextIsCenteredAndHitAreaDoesNotCoverWholeCorridor()
    {
        var placed = LayoutRules.PlaceText(new(36, 2088, 786, 2160), 1.5, 120, 24);
        Assert.Equal(new PixelRect(36, 2106, 216, 2142), placed);
    }

    [Theory]
    [InlineData(-100, 2088, 2124)]
    [InlineData(100, 2124, 2160)]
    public void VerticalOffsetCannotPushTextOutsideTaskbar(double offset, int top, int bottom)
    {
        var placed = LayoutRules.PlaceText(new(36, 2088, 786, 2160), 1.5, 1000, 24, offset);
        Assert.Equal(new PixelRect(36, top, 786, bottom), placed);
    }
}
