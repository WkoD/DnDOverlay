using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>The rescue mark appears and acts on the same screens: every overlay, no window (Part 6).</summary>
public sealed class RescueReachTests
{
    [Fact]
    public void Hide_reaches_every_overlay_and_leaves_a_window_alone()
    {
        var table = new ScreenId("table");
        var wall = new ScreenId("wall");
        var beamer = new ScreenId("beamer");

        var reach = RescueReach.Of([(table, false), (wall, true), (beamer, false)]);

        Assert.Equal([table, beamer], reach);
    }

    [Fact]
    public void A_device_showing_only_windows_has_nothing_to_rescue() =>
        Assert.Empty(RescueReach.Of([(new ScreenId("wall"), true)]));
}
