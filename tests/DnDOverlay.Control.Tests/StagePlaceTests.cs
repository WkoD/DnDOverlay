using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Platform.Windows;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The window goes back where it was - if that place still exists (Part 7): a window restored onto an
/// unplugged monitor runs, answers nothing and cannot be found.
/// </summary>
public sealed class StagePlaceTests
{
    private static readonly IReadOnlyList<MonitorInfo> Desk = [Monitor(0, 0, 1920, 1080), Monitor(1920, 0, 2560, 1440)];

    [Fact]
    public void A_place_on_a_monitor_is_reachable() =>
        Assert.True(StagePlace.Reachable(new WindowPlacement(2200, 100, 1200, 800, false), Desk));

    /// <summary>Half over an edge is where the DM left it - overlapping is enough, being contained is not demanded.</summary>
    [Fact]
    public void A_place_half_over_an_edge_is_reachable() =>
        Assert.True(StagePlace.Reachable(new WindowPlacement(-600, -200, 1200, 800, false), Desk));

    /// <summary>The second monitor unplugged: a place that lay only on it is lost, and the window is left to Windows.</summary>
    [Fact]
    public void A_place_on_an_unplugged_monitor_is_not_reachable() =>
        Assert.False(StagePlace.Reachable(new WindowPlacement(2200, 100, 1200, 800, false), [Monitor(0, 0, 1920, 1080)]));

    /// <summary>Touching an edge is not lying on it - not one pixel of the window could be seen.</summary>
    [Fact]
    public void A_place_touching_an_edge_only_is_not_reachable()
    {
        Assert.False(StagePlace.Reachable(new WindowPlacement(4480, 0, 800, 600, false), Desk));
        Assert.False(StagePlace.Reachable(new WindowPlacement(0, -600, 800, 600, false), Desk));
    }

    [Fact]
    public void Without_monitors_nothing_is_reachable() =>
        Assert.False(StagePlace.Reachable(new WindowPlacement(0, 0, 800, 600, false), []));

    /// <summary>
    /// The Surface at 200 % with a 150 % monitor to its right, both in physical pixels. A place on the second
    /// monitor is reachable while it is plugged in and lost once it is not - even though the same place in the
    /// second monitor's device-independent units (2333 = 3500 / 1.5) would have seemed to overlap the Surface.
    /// </summary>
    [Fact]
    public void At_mixed_scaling_a_place_in_pixels_on_the_unplugged_monitor_is_not_reachable()
    {
        var surface = Monitor(0, 0, 2736, 1824);
        var place = new WindowPlacement(3500, 200, 1200, 800, false);

        Assert.True(StagePlace.Reachable(place, [surface, Monitor(2736, 0, 3840, 2160)]));
        Assert.False(StagePlace.Reachable(place, [surface]));
    }

    /// <summary>The normal position is kept, as left, top, width and height - not the rectangle's corners.</summary>
    [Fact]
    public void A_placement_is_kept_as_its_normal_position() =>
        Assert.Equal(new WindowPlacement(3500, 200, 1200, 800, false), StagePlace.Kept(Rect(3500, 200, 4700, 1000), showCmd: 1));

    /// <summary>Maximised is kept; minimised is not maximised and comes back at its normal position, as before.</summary>
    [Fact]
    public void Only_a_maximised_placement_is_kept_as_maximised()
    {
        Assert.True(StagePlace.Kept(Rect(0, 0, 800, 600), showCmd: 3).Maximised);
        Assert.False(StagePlace.Kept(Rect(0, 0, 800, 600), showCmd: 2).Maximised);
        Assert.False(StagePlace.Kept(Rect(0, 0, 800, 600), showCmd: 1).Maximised);
    }

    /// <summary>A file from before the change holds device-independent units, possibly fractional: rounded, read as pixels.</summary>
    [Fact]
    public void A_kept_place_is_put_back_as_whole_pixels()
    {
        var rect = StagePlace.Normal(new WindowPlacement(120.4, 59.6, 1399.5, 900.2, false));

        Assert.Equal((120, 60, 1520, 960), (rect.Left, rect.Top, rect.Right, rect.Bottom));
    }

    /// <summary>What is taken is what is put back, unchanged, so a window kept and restored stands where it stood.</summary>
    [Fact]
    public void A_place_taken_and_put_back_is_the_same_rectangle()
    {
        var rect = StagePlace.Normal(StagePlace.Kept(Rect(-1400, 120, -200, 900), showCmd: 1));

        Assert.Equal((-1400, 120, -200, 900), (rect.Left, rect.Top, rect.Right, rect.Bottom));
    }

    private static StagePlace.NativeRect Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    private static MonitorInfo Monitor(int left, int top, int width, int height) =>
        new(new ScreenInfo(new ScreenId($"M{left}"), $"M{left}", null, new PixelSize(width, height), 96, left == 0), left, top, width, height);
}
