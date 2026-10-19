namespace DnDOverlay.Core.Tests;

/// <summary>
/// The reasons mark of a tile (Part 7): five reasons, most fundamental first, never what the DM set
/// himself, and the cause named once.
/// </summary>
public sealed class ScreenReasonsTests
{
    private static readonly DeviceId Device = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"));
    private static readonly ScreenRef Screen = new(Device, new ScreenId("TISCH//DISPLAY1"));

    [Fact]
    public void A_played_screen_has_none_whatever_its_state()
    {
        foreach (var state in new[] { ScreenState.Enabled, ScreenState.Disabled, ScreenState.Diagnostic, ScreenState.Inactive })
        {
            Assert.Empty(ScreenReasons.Of(View(state), Here(true)));
        }

        Assert.Null(ScreenReasons.Mark([]));
    }

    [Fact]
    public void A_screen_of_an_unpaired_device_says_so()
    {
        Assert.Equal([ScreenReason.NotPaired], ScreenReasons.Of(View(), device: null));
    }

    /// <summary>The hub calls the screens of a missing device unavailable; the tile names the device instead, once.</summary>
    [Fact]
    public void A_device_that_is_not_here_is_the_reason_not_its_screens()
    {
        Assert.Equal(
            [ScreenReason.DeviceNotConnected],
            ScreenReasons.Of(View(suppressed: SuppressReason.Unavailable), Here(false)));
    }

    [Fact]
    public void A_screen_gone_from_a_connected_device_is_unavailable()
    {
        Assert.Equal(
            [ScreenReason.ScreenUnavailable],
            ScreenReasons.Of(View(suppressed: SuppressReason.Unavailable), Here(true)));
    }

    /// <summary>Step 32e: the control window over the screen and the device unplugged - two reasons, device first.</summary>
    [Fact]
    public void A_device_reason_and_a_screen_finding_stand_together()
    {
        var reasons = ScreenReasons.Of(View(suppressed: SuppressReason.ControlWindow), Here(false));

        Assert.Equal([ScreenReason.DeviceNotConnected, ScreenReason.ControlWindow], reasons);
        Assert.Equal("⚠ 2 reasons", ScreenReasons.Mark(reasons));
    }

    [Fact]
    public void One_reason_is_counted_in_the_singular()
    {
        Assert.Equal("⚠ 1 reason", ScreenReasons.Mark([ScreenReason.HiddenAtDevice]));
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(10, true)]
    [InlineData(3, true)]
    public void A_battery_reads_as_low_from_ten_percent_down(int percent, bool low)
    {
        Assert.Equal(low, new BatteryInfo(percent, PluggedIn: false).Low);
    }

    private static ScreenView View(ScreenState state = ScreenState.Enabled, SuppressReason? suppressed = null) =>
        new(Screen, new ScreenInfo(Screen.Screen, "TISCH//DISPLAY1", null, new PixelSize(1920, 1080), 96, true), state, suppressed);

    private static DeviceView Here(bool connected) => new(Device, "TISCH-PC", connected, []);
}
