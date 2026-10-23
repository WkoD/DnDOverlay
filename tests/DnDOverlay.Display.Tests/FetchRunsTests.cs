using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// One fetch run per screen; asks while it runs make it look again rather than queue runs, and no ask
/// is ever dropped (hand-runs of M3b: <c>Peak=1</c> while every picture had a run of its own).
/// </summary>
public sealed class FetchRunsTests
{
    private static readonly ScreenId Table = new("table");
    private static readonly ScreenId Wall = new("wall");

    [Fact]
    public void The_first_ask_starts_a_run()
    {
        Assert.True(new FetchRuns().Ask(Table));
    }

    /// <summary>Twenty arriving pictures make one run that looks again, not twenty runs.</summary>
    [Fact]
    public void Asks_while_a_run_is_going_mark_it_to_look_again()
    {
        var runs = new FetchRuns();
        _ = runs.Ask(Table);
        runs.Looking(Table);

        for (var i = 0; i < 20; i++)
        {
            Assert.False(runs.Ask(Table));
        }

        Assert.True(runs.AskedAgain(Table));

        runs.Looking(Table);

        Assert.False(runs.AskedAgain(Table));
    }

    /// <summary>Each screen has its own run.</summary>
    [Fact]
    public void Another_screen_starts_its_own_run()
    {
        var runs = new FetchRuns();
        _ = runs.Ask(Table);

        Assert.True(runs.Ask(Wall));
    }

    /// <summary>A run that finishes with nothing asked meanwhile lets go, and the next ask starts afresh.</summary>
    [Fact]
    public void A_finished_run_lets_the_next_ask_start_one()
    {
        var runs = new FetchRuns();
        _ = runs.Ask(Table);
        runs.Looking(Table);

        Assert.False(runs.Finished(Table));
        Assert.True(runs.Ask(Table));
    }

    /// <summary>
    /// The window of a few instructions between the run's last look and its letting go: an ask landing
    /// there is said by the finishing run, so the caller asks again and a new run starts.
    /// </summary>
    [Fact]
    public void An_ask_between_the_last_look_and_letting_go_is_not_lost()
    {
        var runs = new FetchRuns();
        _ = runs.Ask(Table);
        runs.Looking(Table);
        Assert.False(runs.AskedAgain(Table));

        Assert.False(runs.Ask(Table));

        Assert.True(runs.Finished(Table));
        Assert.True(runs.Ask(Table));
    }

    /// <summary>
    /// Under real threads, driven the way the application drives it: whatever the interleaving, never
    /// two runs at once, and the last ask is followed by a round that began after it - no picture waits
    /// for an ask that was swallowed.
    /// </summary>
    [Fact]
    public async Task No_ask_is_lost_under_contention()
    {
        const int Asks = 20_000;

        var runs = new FetchRuns();
        var started = new System.Collections.Concurrent.ConcurrentQueue<Task>();
        var lastAsk = 0L;
        var lastLook = 0L;
        var clock = 0L;
        var active = 0;
        var overlapped = false;

        void Run()
        {
            while (true)
            {
                if (Interlocked.Increment(ref active) > 1)
                {
                    overlapped = true;
                }

                do
                {
                    runs.Looking(Table);
                    Volatile.Write(ref lastLook, Interlocked.Increment(ref clock));
                }
                while (runs.AskedAgain(Table));

                _ = Interlocked.Decrement(ref active);

                // As the application does in its finally: asked meanwhile, so ask again.
                if (!runs.Finished(Table) || !runs.Ask(Table))
                {
                    return;
                }
            }
        }

        await Task.Run(
            () =>
            {
                for (var i = 0; i < Asks; i++)
                {
                    Volatile.Write(ref lastAsk, Interlocked.Increment(ref clock));

                    if (runs.Ask(Table))
                    {
                        started.Enqueue(Task.Run(Run, TestContext.Current.CancellationToken));
                    }
                }
            },
            TestContext.Current.CancellationToken);

        // Every run was started by the loop above, so once they have all ended nothing is going.
        await Task.WhenAll(started);

        Assert.False(overlapped);
        Assert.True(Volatile.Read(ref lastLook) > Volatile.Read(ref lastAsk));
    }
}
