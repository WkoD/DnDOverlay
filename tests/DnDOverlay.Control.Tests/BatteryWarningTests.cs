using DnDOverlay.Core;
using Step = DnDOverlay.Control.BatteryWarning.Step;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The one real battery warning (DM, 19.10.2026): said once at the red line, gone with the cable, and
/// back only after the battery has been above the line in between (Part 7).
/// </summary>
public sealed class BatteryWarningTests
{
    [Fact]
    public void At_the_line_it_warns_once_and_then_keeps_quiet()
    {
        var warning = new BatteryWarning();

        Assert.Equal(Step.Warn, warning.Look(new BatteryInfo(BatteryInfo.LowPercent, PluggedIn: false)));
        Assert.Equal(Step.Nothing, warning.Look(new BatteryInfo(BatteryInfo.LowPercent - 1, PluggedIn: false)));
        Assert.Equal(Step.Nothing, warning.Look(new BatteryInfo(3, PluggedIn: false)));
    }

    /// <summary>One percent above the line is not low: nothing is said, and a standing warning would go.</summary>
    [Fact]
    public void Above_the_line_it_does_not_warn()
    {
        var warning = new BatteryWarning();

        Assert.Equal(Step.Withdraw, warning.Look(new BatteryInfo(BatteryInfo.LowPercent + 1, PluggedIn: false)));
    }

    /// <summary>The cable goes in: the warning goes. Pulled again while still low, it comes again - the cable re-arms it.</summary>
    [Fact]
    public void The_cable_takes_the_warning_away_and_arms_it_again()
    {
        var warning = new BatteryWarning();

        Assert.Equal(Step.Warn, warning.Look(new BatteryInfo(8, PluggedIn: false)));
        Assert.Equal(Step.Withdraw, warning.Look(new BatteryInfo(8, PluggedIn: true)));
        Assert.Equal(Step.Warn, warning.Look(new BatteryInfo(8, PluggedIn: false)));
    }

    /// <summary>Back above the line and down again: a second warning, because the battery was fine in between.</summary>
    [Fact]
    public void Back_above_the_line_and_down_again_warns_again()
    {
        var warning = new BatteryWarning();

        Assert.Equal(Step.Warn, warning.Look(new BatteryInfo(10, PluggedIn: false)));
        Assert.Equal(Step.Withdraw, warning.Look(new BatteryInfo(11, PluggedIn: false)));
        Assert.Equal(Step.Warn, warning.Look(new BatteryInfo(10, PluggedIn: false)));
    }

    /// <summary>A machine without a battery - a desk PC as hub - never warns, and an unknown reading re-arms.</summary>
    [Fact]
    public void No_battery_never_warns()
    {
        var warning = new BatteryWarning();

        Assert.Equal(Step.Withdraw, warning.Look(null));
        Assert.Equal(Step.Warn, warning.Look(new BatteryInfo(5, PluggedIn: false)));
        Assert.Equal(Step.Withdraw, warning.Look(null));
        Assert.Equal(Step.Warn, warning.Look(new BatteryInfo(5, PluggedIn: false)));
    }
}
