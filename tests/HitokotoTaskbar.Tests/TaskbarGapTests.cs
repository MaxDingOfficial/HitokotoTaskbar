using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.Tests;

public sealed class TaskbarGapTests
{
    private static readonly PixelRect Bar = new(0, 1040, 1920, 1080);

    [Fact]
    public void CenteredStartLeavesOnlyTheGapBeforeItsControlClearance()
    {
        var gap = TaskbarGapRules.FindBeforeStart(Bar, 800, [new(800, 1040, 850, 1080), new(1800, 1040, 1920, 1080)], 1);
        Assert.Equal(new PixelRect(0, 1040, 792, 1080), gap);
    }

    [Fact]
    public void LeftWidgetsAreSkippedWithScaledClearance()
    {
        var gap = TaskbarGapRules.FindBeforeStart(Bar, 600, [new(0, 1040, 120, 1080)], 1.5);
        Assert.Equal(new PixelRect(132, 1040, 588, 1080), gap);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(24)]
    public void LeftAlignedStartLeavesNoSafeOverlay(int start)
    {
        Assert.True(TaskbarGapRules.FindBeforeStart(Bar, start, [], 1).IsEmpty);
    }

    [Fact]
    public void OverlappingUnsortedControlsCannotCreateAFakeGap()
    {
        var controls = new[]
        {
            new PixelRect(400, 1040, 500, 1080), new PixelRect(80, 1040, 150, 1080),
            new PixelRect(20, 1040, 100, 1080), new PixelRect(405, 1040, 495, 1080)
        };
        var gap = TaskbarGapRules.FindBeforeStart(Bar, 800, controls, 1);
        Assert.Equal(new PixelRect(508, 1040, 792, 1080), gap);
        Assert.All(controls, control => Assert.True(gap.Right <= control.Left - 8 || gap.Left >= control.Right + 8));
    }

    [Fact]
    public void ChoosesWidestGapWhenAnIsolatedControlDividesTheLeftSide()
    {
        var gap = TaskbarGapRules.FindBeforeStart(Bar, 1000, [new(700, 1040, 800, 1080)], 1);
        Assert.Equal(new PixelRect(0, 1040, 692, 1080), gap);
    }

    [Fact]
    public void SecondaryMonitorKeepsNegativeOriginAndCorrectDipClearance()
    {
        var gap = TaskbarGapRules.FindBeforeStart(new(-3840, 2080, 0, 2160), -2000,
            [new(-3830, 2080, -3680, 2160)], 2);
        Assert.Equal(new PixelRect(-3664, 2080, -2016, 2160), gap);
    }

    [Fact]
    public void OffTaskbarAndEmptyRectanglesDoNotConsumeTheGap()
    {
        var gap = TaskbarGapRules.FindBeforeStart(Bar, 800,
            [new(0, 0, 600, 900), new(-400, 1040, -1, 1080), new(30, 1040, 30, 1080), default], 1);
        Assert.Equal(new PixelRect(0, 1040, 792, 1080), gap);
    }

    [Fact]
    public void ControlsCoveringEveryCandidateLeaveNoGap()
    {
        var gap = TaskbarGapRules.FindBeforeStart(Bar, 800, [new(-50, 1030, 900, 1100)], 1);
        Assert.True(gap.IsEmpty);
    }

    [Fact]
    public void InsufficientGapIsJudgedInDipAtCurrentMonitorScale()
    {
        Assert.False(TaskbarGapRules.FindBeforeStart(Bar, 80, [], 1).IsEmpty);
        Assert.True(TaskbarGapRules.FindBeforeStart(Bar, 80, [], 2).IsEmpty);
    }

    [Fact]
    public void InvalidGeometryCannotProduceAnOverlay()
    {
        Assert.True(TaskbarGapRules.FindBeforeStart(default, 0, [], 1).IsEmpty);
        Assert.True(TaskbarGapRules.FindBeforeStart(Bar, 2000, [], 1).IsEmpty);
        Assert.True(TaskbarGapRules.FindBeforeStart(Bar, 800, [], double.NaN).IsEmpty);
        Assert.True(TaskbarGapRules.FindBeforeStart(Bar, 800, [], 0).IsEmpty);
    }
}
