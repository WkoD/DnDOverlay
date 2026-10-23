namespace DnDOverlay.Display.Tests;

/// <summary>The rescue mark stands exactly while this device would answer a call, and says so once per absence (Part 6).</summary>
public sealed class RescueMarkTests
{
    [Fact]
    public void Nothing_stands_while_the_grace_period_runs()
    {
        Assert.Null(RescueMark.Minutes(due: false, absent: TimeSpan.FromMinutes(5)));
    }

    /// <summary>A device never connected in this run has nobody missing - no mark, whatever the clock says.</summary>
    [Fact]
    public void Nothing_stands_on_a_device_never_connected()
    {
        Assert.Null(RescueMark.Minutes(due: true, absent: null));
    }

    /// <summary>Whole minutes, and never "0 min": the grace period may be shorter than a minute.</summary>
    [Theory]
    [InlineData(10, 1)]
    [InlineData(59, 1)]
    [InlineData(60, 1)]
    [InlineData(179, 2)]
    [InlineData(3600, 60)]
    public void The_absence_is_said_in_whole_minutes_at_least_one(int seconds, int minutes)
    {
        Assert.Equal(minutes, RescueMark.Minutes(due: true, absent: TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>Once per absence, however many seconds it stands.</summary>
    [Fact]
    public void The_mark_comes_up_once_per_absence()
    {
        var mark = new RescueMark();

        Assert.True(mark.CameUp(standing: true, overlays: 2));
        Assert.False(mark.CameUp(standing: true, overlays: 2));
        Assert.False(mark.CameUp(standing: true, overlays: 1));
    }

    /// <summary>Without an overlay to stand on it has not come up - it does once the first overlay is there.</summary>
    [Fact]
    public void Without_an_overlay_the_mark_waits_for_one()
    {
        var mark = new RescueMark();

        Assert.False(mark.CameUp(standing: true, overlays: 0));
        Assert.True(mark.CameUp(standing: true, overlays: 1));
    }

    /// <summary>The control coming back ends the absence; the next one says so again.</summary>
    [Fact]
    public void A_new_absence_comes_up_again()
    {
        var mark = new RescueMark();

        _ = mark.CameUp(standing: true, overlays: 1);
        Assert.False(mark.CameUp(standing: false, overlays: 1));

        Assert.True(mark.CameUp(standing: true, overlays: 1));
    }
}
