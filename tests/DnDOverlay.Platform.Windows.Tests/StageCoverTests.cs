using DnDOverlay.Core;
using DnDOverlay.Platform.Windows;

namespace DnDOverlay.Platform.Windows.Tests;

/// <summary>
/// Which screens the control window lies on. A function over rectangles - no window is asked and
/// none is moved, which is why these can exist at all: the wiring around it lives in the WPF
/// application, where nothing runs (Part 2).
/// <para>
/// The case this file was written for is the second one. "The monitor with the largest overlap"
/// is the obvious rule and the wrong one: a window lying 60 % on A and 40 % on B would leave B to
/// be played on, and the overlay would drop onto the remaining 40 % of the DM's stage - exactly
/// what the rule exists to prevent.
/// </para>
/// </summary>
public sealed class StageCoverTests
{
    private static readonly MonitorInfo Left = Monitor("LEFT", 0, 0, 2560, 1440);
    private static readonly MonitorInfo Right = Monitor("RIGHT", 2560, 0, 1920, 1080);
    private static readonly MonitorInfo Above = Monitor("ABOVE", 0, -1080, 1920, 1080);

    [Fact]
    public void A_window_on_one_monitor_covers_that_one()
    {
        var covered = StageCover.Covered((100, 100, 800, 600), [Left, Right, Above]);

        Assert.Equal([Id("LEFT")], covered);
    }

    /// <summary>
    /// The whole decision, and the reason it is not "the biggest overlap": both are blocked, so no
    /// overlay can land on the part of the stage that is still visible.
    /// </summary>
    [Fact]
    public void A_window_across_the_border_covers_both_however_lopsided()
    {
        // 60 % on the left monitor, 40 % on the right.
        var covered = StageCover.Covered((2260, 100, 500, 600), [Left, Right]);

        Assert.Equal([Id("LEFT"), Id("RIGHT")], covered);
    }

    /// <summary>
    /// Flush against the border is NOT on it. Without the strictness a window snapped to the edge
    /// would block the next monitor, and the DM would read "covered" on a screen with nothing on it.
    /// </summary>
    [Fact]
    public void Touching_the_edge_is_not_lying_on_it()
    {
        var covered = StageCover.Covered((1560, 0, 1000, 1080), [Left, Right]);

        Assert.Equal([Id("LEFT")], covered);
    }

    /// <summary>
    /// Negative coordinates are the ordinary case, not an edge case: a monitor placed above or to
    /// the left of the primary one has them, and so does a window on it.
    /// </summary>
    [Fact]
    public void A_monitor_above_the_primary_one_is_found_like_any_other()
    {
        var covered = StageCover.Covered((200, -900, 400, 300), [Left, Right, Above]);

        Assert.Equal([Id("ABOVE")], covered);
    }

    /// <summary>
    /// Minimised comes through as an empty rectangle, and it must free every screen: the DM put
    /// the window away, so there is nothing left to cover.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    public void A_window_with_no_area_covers_nothing(int width, int height)
    {
        Assert.Empty(StageCover.Covered((100, 100, width, height), [Left, Right, Above]));
    }

    [Fact]
    public void Without_monitors_there_is_nothing_to_cover()
    {
        Assert.Empty(StageCover.Covered((100, 100, 800, 600), []));
    }

    /// <summary>
    /// A window larger than the desktop - dragged half off the screen, or spanning everything -
    /// blocks all of them rather than none.
    /// </summary>
    [Fact]
    public void A_window_over_everything_covers_everything()
    {
        var covered = StageCover.Covered((-500, -2000, 6000, 4000), [Left, Right, Above]);

        Assert.Equal(3, covered.Count);
    }

    private static ScreenId Id(string name) => new($@"\\?\DISPLAY#{name}");

    private static MonitorInfo Monitor(string name, int x, int y, int width, int height) =>
        new(
            new ScreenInfo(Id(name), $"PC//{name}", null, new PixelSize(width, height), 96, IsPrimary: false),
            x,
            y,
            width,
            height);
}
