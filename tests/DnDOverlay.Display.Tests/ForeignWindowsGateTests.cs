using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>The windows of other programs: reported when asked and allowed, laid only where the DM may (Part 6).</summary>
public sealed class ForeignWindowsGateTests
{
    [Theory]
    [InlineData(true, ForeignWindowsMode.ReportAndControl, true)]
    [InlineData(true, ForeignWindowsMode.ReportOnly, true)]
    [InlineData(true, ForeignWindowsMode.Off, false)]
    [InlineData(false, ForeignWindowsMode.ReportAndControl, false)]
    public void The_list_goes_out_when_asked_for_and_reported_at_all(bool wanted, ForeignWindowsMode mode, bool reports)
    {
        Assert.Equal(reports, ForeignWindowsGate.Reports(wanted, mode));
    }

    /// <summary>A device that only reports, or reports nothing, lays no window - whatever the screen.</summary>
    [Theory]
    [InlineData(ForeignWindowsMode.ReportOnly)]
    [InlineData(ForeignWindowsMode.Off)]
    public void Only_report_and_control_lays_a_window(ForeignWindowsMode mode)
    {
        Assert.Equal(WindowOutcome.NotAllowed, ForeignWindowsGate.Refusal(mode, screenKnown: false, inactive: true));
        Assert.Equal(WindowOutcome.NotAllowed, ForeignWindowsGate.Refusal(mode, screenKnown: true, inactive: false));
    }

    [Fact]
    public void A_screen_this_device_does_not_have_is_gone()
    {
        Assert.Equal(WindowOutcome.Gone, ForeignWindowsGate.Refusal(ForeignWindowsMode.ReportAndControl, screenKnown: false, inactive: false));
    }

    /// <summary>The DM handed an inactive screen back to Windows; somebody may be typing there.</summary>
    [Fact]
    public void An_inactive_screen_is_left_alone()
    {
        Assert.Equal(WindowOutcome.Inactive, ForeignWindowsGate.Refusal(ForeignWindowsMode.ReportAndControl, screenKnown: true, inactive: true));
    }

    [Fact]
    public void A_playing_screen_under_report_and_control_is_laid()
    {
        Assert.Null(ForeignWindowsGate.Refusal(ForeignWindowsMode.ReportAndControl, screenKnown: true, inactive: false));
    }
}
