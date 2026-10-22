using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// When the log panel draws its bar and lines again: on a change it shows, never on a beat. Every
/// device event reaches it, battery ticks included (Part 1: a view of the load must not become part
/// of it).
/// </summary>
public sealed class LogPanelTests
{
    private static readonly DeviceId Device = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000d1"));

    [Fact]
    public void A_battery_tick_or_a_round_trip_changes_nothing_the_panel_shows()
    {
        var before = Table();
        var after = Table() with { Battery = new BatteryInfo(41, PluggedIn: false), RoundTrip = TimeSpan.FromMilliseconds(12) };

        Assert.Equal(LogPanel.Sources([before]), LogPanel.Sources([after]));
    }

    [Fact]
    public void A_name_a_connection_or_a_screen_label_is_a_change()
    {
        var before = LogPanel.Sources([Table()]);

        Assert.NotEqual(before, LogPanel.Sources([Table() with { Name = "BEAMER" }]));
        Assert.NotEqual(before, LogPanel.Sources([Table() with { Connected = false }]));
        Assert.NotEqual(before, LogPanel.Sources([Table("Wand")]));
        Assert.NotEqual(before, LogPanel.Sources([]));
    }

    private static DeviceView Table(string label = "TISCH-PC//1") =>
        new(
            Device,
            "TISCH-PC",
            Connected: true,
            [new ScreenView(new ScreenRef(Device, new ScreenId("DISPLAY1")), new ScreenInfo(new ScreenId("DISPLAY1"), label, null, new PixelSize(1920, 1080), 96, true), ScreenState.Enabled)]);
}
