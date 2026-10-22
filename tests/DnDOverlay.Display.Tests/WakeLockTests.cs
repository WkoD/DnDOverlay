namespace DnDOverlay.Display.Tests;

/// <summary>
/// The screens stay on while a control is connected and the parameter wants it, and are let go the
/// moment either stops being true (Part 6).
/// </summary>
public sealed class WakeLockTests
{
    /// <summary>A device that has never met a control holds nothing - the request takes a connection as well.</summary>
    [Fact]
    public void Nothing_is_held_before_a_control_connects()
    {
        using var ui = new DispatcherThread();
        var bench = new Bench(ui);

        bench.Lock.Wanted = true;

        Assert.Empty(bench.Requests);
        Assert.Empty(bench.Reports);
    }

    [Fact]
    public void A_connection_holds_the_screens_on()
    {
        using var ui = new DispatcherThread();
        var bench = new Bench(ui);

        bench.Lock.Connected = true;

        Assert.Equal([true], bench.Requests);
        Assert.Equal([true], bench.Reports);
    }

    /// <summary>
    /// The request belongs to the thread it was made on and dies with it, so it is made on the UI
    /// thread - the one thread alive exactly as long as the application.
    /// </summary>
    [Fact]
    public void The_request_is_made_on_the_ui_thread()
    {
        using var ui = new DispatcherThread();
        var bench = new Bench(ui);

        bench.Lock.Connected = true;

        Assert.Equal([ui.ThreadId], bench.Threads);
    }

    /// <summary>Every arriving message says "connected" again; the request only moves when the answer changes.</summary>
    [Fact]
    public void Saying_connected_again_asks_nothing_more()
    {
        using var ui = new DispatcherThread();
        var bench = new Bench(ui);

        bench.Lock.Connected = true;
        bench.Lock.Connected = true;
        bench.Lock.Connected = true;

        Assert.Equal([true], bench.Requests);
        Assert.Equal([true], bench.Reports);
    }

    /// <summary>The switch from afar lets go at once, and the connection ending does so too.</summary>
    [Fact]
    public void Switched_off_or_disconnected_lets_go()
    {
        using var ui = new DispatcherThread();
        var bench = new Bench(ui);

        bench.Lock.Connected = true;
        bench.Lock.Wanted = false;
        bench.Lock.Wanted = true;
        bench.Lock.Connected = false;

        Assert.Equal([true, false, true, false], bench.Requests);
        Assert.Equal([true, false, true, false], bench.Reports);
        Assert.True(bench.Lock.Wanted);
    }

    /// <summary>A switched-off parameter keeps a connection from holding anything.</summary>
    [Fact]
    public void Not_wanted_holds_nothing_although_connected()
    {
        using var ui = new DispatcherThread();
        var bench = new Bench(ui);

        bench.Lock.Wanted = false;
        bench.Lock.Connected = true;

        Assert.Empty(bench.Requests);
    }

    /// <summary>The way out lets go of a held request, and says nothing when nothing was held.</summary>
    [Fact]
    public void The_way_out_lets_go_of_what_is_held()
    {
        using var ui = new DispatcherThread();
        var held = new Bench(ui);
        var idle = new Bench(ui);

        held.Lock.Connected = true;
        held.Lock.Dispose();
        idle.Lock.Dispose();

        Assert.Equal([true, false], held.Requests);
        Assert.Empty(idle.Requests);
    }

    private sealed class Bench
    {
        internal Bench(DispatcherThread ui)
        {
            Lock = new WakeLock(
                ui.Dispatcher,
                Reports.Add,
                hold =>
                {
                    Requests.Add(hold);
                    Threads.Add(Environment.CurrentManagedThreadId);
                });
        }

        internal WakeLock Lock { get; }

        internal List<bool> Requests { get; } = [];

        internal List<bool> Reports { get; } = [];

        internal List<int> Threads { get; } = [];
    }
}
