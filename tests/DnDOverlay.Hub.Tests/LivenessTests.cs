using System.Collections.Concurrent;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The heartbeat and the clone probe, which are the same question asked for two different reasons
/// (Part 4).
/// <para>
/// Against the hand-written clock, so twelve seconds of silence are checked in microseconds and
/// exactly rather than "probably long enough" (rule 10). It is also the only way to check this at
/// all: a test that slept would be slow AND flaky.
/// </para>
/// </summary>
public sealed class LivenessTests
{
    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(5);

    [Fact(Timeout = 30_000)]
    public async Task A_connection_that_says_nothing_is_given_up()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTime();
        var pings = new ConcurrentQueue<PingMessage>();
        var liveness = Watching(time, pings);

        var watch = liveness.WatchAsync(cancellationToken);

        time.Advance(Beat);
        await Until(() => pings.Count == 1, cancellationToken);
        Assert.False(watch.IsCompleted);

        // Ten seconds of silence is under the ceiling: this is the length of Wi-Fi dropout that
        // must NOT count as a disconnection.
        time.Advance(Beat);
        await Until(() => pings.Count == 2, cancellationToken);
        Assert.False(watch.IsCompleted);

        time.Advance(Beat);

        await watch;
    }

    /// <summary>
    /// A silence deadline rather than a count of unanswered pings, and the difference shows here:
    /// a device that is busy sending is alive whether or not a <c>Pong</c> happened to cross the
    /// wire.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_device_that_keeps_talking_is_never_given_up()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTime();
        var pings = new ConcurrentQueue<PingMessage>();
        var liveness = Watching(time, pings);

        var watch = liveness.WatchAsync(cancellationToken);

        for (var beat = 1; beat <= 10; beat++)
        {
            liveness.Note();
            time.Advance(Beat);

            await Until(() => pings.Count == beat, cancellationToken);
        }

        Assert.False(watch.IsCompleted);
    }

    /// <summary>
    /// The probe that tells a clone from a crashed display coming straight back. An answer means
    /// there really are two machines - decided on an ANSWER, not on a deadline.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task An_answered_probe_says_the_other_end_is_still_there()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTime();
        var pings = new ConcurrentQueue<PingMessage>();
        var liveness = Watching(time, pings);

        var probe = liveness.ProbeAsync(TimeSpan.FromSeconds(1), cancellationToken);

        Assert.Single(pings);
        liveness.NotePong();

        Assert.True(await probe);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_probe_that_stays_unanswered_says_the_connection_may_be_replaced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTime();
        var pings = new ConcurrentQueue<PingMessage>();
        var liveness = Watching(time, pings);

        var probe = liveness.ProbeAsync(TimeSpan.FromSeconds(1), cancellationToken);

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.False(await probe);
    }

    /// <summary>
    /// The ping carries the round trip the control measured, so both sides show the same number
    /// instead of each working one out its own way (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_ping_carries_back_the_round_trip_that_was_measured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTime();
        var pings = new ConcurrentQueue<PingMessage>();
        var liveness = Watching(time, pings);

        // Nothing has been measured yet, so there is nothing to claim.
        var first = liveness.ProbeAsync(TimeSpan.FromSeconds(1), cancellationToken);

        Assert.Null(Assert.Single(pings).RoundTripMs);

        time.Advance(TimeSpan.FromMilliseconds(40));
        liveness.NotePong();

        Assert.True(await first);

        var second = liveness.ProbeAsync(TimeSpan.FromSeconds(1), cancellationToken);

        Assert.Equal(40, pings.Last().RoundTripMs);

        liveness.NotePong();
        Assert.True(await second);
    }

    private static Liveness Watching(TimeProvider time, ConcurrentQueue<PingMessage> pings) =>
        new(time,
            new HubOptions { HeartbeatInterval = Beat, SilenceBeforeDead = TimeSpan.FromSeconds(12) },
            message =>
            {
                pings.Enqueue((PingMessage)message);

                return true;
            });

    /// <summary>
    /// The manual clock fires its timers synchronously, but what they wake up runs on the thread
    /// pool - so the test waits for the effect rather than for a duration.
    /// </summary>
    private static async Task Until(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}
