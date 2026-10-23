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

    private static MonitorInfo Monitor(int left, int top, int width, int height) =>
        new(new ScreenInfo(new ScreenId($"M{left}"), $"M{left}", null, new PixelSize(width, height), 96, left == 0), left, top, width, height);
}
