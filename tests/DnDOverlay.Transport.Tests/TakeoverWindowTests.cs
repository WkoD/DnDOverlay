namespace DnDOverlay.Transport.Tests;

/// <summary>
/// When a display answers a call for orphaned displays: connected in this run, lost without a
/// goodbye, and longer ago than the rescue mark's minute (Part 4). Each condition gets the case
/// where it alone keeps the window shut.
/// </summary>
public sealed class TakeoverWindowTests
{
    [Fact]
    public void A_connection_lost_without_a_goodbye_a_minute_ago_opens_the_window()
    {
        var clock = new Clock();
        var window = new TakeoverWindow(clock);

        window.Connected();
        window.Lost();
        clock.Advance(TakeoverWindow.DefaultGrace);

        Assert.True(window.IsOpen);
    }

    /// <summary>
    /// The ordinary state of an autostarting display: no control most of the time. Were that open,
    /// every display in the house would offer itself to every call.
    /// </summary>
    [Fact]
    public void A_display_that_was_never_connected_does_not_answer()
    {
        var clock = new Clock();
        var window = new TakeoverWindow(clock);

        window.Lost();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.False(window.IsOpen);
    }

    /// <summary>A Wi-Fi hiccup must not make a display answerable.</summary>
    [Fact]
    public void Within_the_minute_the_window_stays_shut()
    {
        var clock = new Clock();
        var window = new TakeoverWindow(clock);

        window.Connected();
        window.Lost();
        clock.Advance(TakeoverWindow.DefaultGrace - TimeSpan.FromSeconds(1));

        Assert.False(window.IsOpen);
    }

    /// <summary>A control that said goodbye is not missing - it will come back, and until then this device has nothing to offer anybody.</summary>
    [Fact]
    public void A_goodbye_keeps_the_window_shut()
    {
        var clock = new Clock();
        var window = new TakeoverWindow(clock);

        window.Connected();
        window.SaidGoodbye();
        window.Lost();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.False(window.IsOpen);
    }

    [Fact]
    public void A_new_connection_shuts_the_window_again()
    {
        var clock = new Clock();
        var window = new TakeoverWindow(clock);

        window.Connected();
        window.Lost();
        clock.Advance(TimeSpan.FromMinutes(5));
        window.Connected();

        Assert.False(window.IsOpen);
    }

    /// <summary>
    /// The minute counts from the first loss. A failed reconnect inside it is the same absence, not
    /// a new one - otherwise a display retrying every few seconds would never become answerable.
    /// </summary>
    [Fact]
    public void A_second_loss_does_not_restart_the_minute()
    {
        var clock = new Clock();
        var window = new TakeoverWindow(clock);

        window.Connected();
        window.Lost();
        clock.Advance(TimeSpan.FromSeconds(40));
        window.Lost();
        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.True(window.IsOpen);
    }

    /// <summary>A goodbye belongs to the connection it ended; the next one starts clean.</summary>
    [Fact]
    public void After_a_goodbye_the_next_connection_can_open_it_again()
    {
        var clock = new Clock();
        var window = new TakeoverWindow(clock);

        window.Connected();
        window.SaidGoodbye();
        window.Connected();
        window.Lost();
        clock.Advance(TakeoverWindow.DefaultGrace);

        Assert.True(window.IsOpen);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        internal void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
