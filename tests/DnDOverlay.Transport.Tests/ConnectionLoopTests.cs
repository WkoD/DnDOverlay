using System.Collections.Concurrent;
using System.Net;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The decisions that turn single connections into a reconnect - which address, how long to wait,
/// when to start over. They lived in the display application until 17.10.2026, where no test ran,
/// and that is where M1b let a display through that never connected again.
/// <para>
/// No network and no real waiting: the connection is a script of outcomes, discovery a function,
/// and the clock records every wait it is asked for and lets it pass at once.
/// </para>
/// </summary>
public sealed class ConnectionLoopTests
{
    private static readonly HubAddress Preferred = new("tisch-control", 4009);

    private static readonly Sighting Heard = new(
        new Beacon(Guid.NewGuid(), "Control", 4123, Protocol.Version),
        IPAddress.Parse("192.168.178.20"));

    private readonly InstantTime _time = new();
    private readonly Captured _log = new();

    [Fact(Timeout = 10_000)]
    public async Task A_configured_host_is_tried_first_without_listening()
    {
        var script = new Script(Attempt.Fruitless);

        await Run(Preferred, script, TestContext.Current.CancellationToken);

        Assert.Equal(Preferred, script.Targets[0]);
        Assert.Equal(0, script.ListenedBeforeFirstConnect);
    }

    /// <summary>
    /// A preferred address, not an exclusive one: it changes when the Surface moves between Wi-Fi
    /// and its dock. After it fails, discovery gets its turn - bounded, or a control that was merely
    /// restarting would never be tried on the old address again.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task After_the_configured_host_fails_discovery_is_asked_and_bounded()
    {
        var script = new Script(Attempt.Fruitless, Attempt.Fruitless);

        await Run(Preferred, script, TestContext.Current.CancellationToken);

        Assert.Equal(new HubAddress("192.168.178.20", 4123), script.Targets[1]);
        Assert.Contains(ConnectionLoop.PreferredHostPatience, _time.Requested);
    }

    /// <summary>The sender of the beacon is knocked on, with the port the beacon names.</summary>
    [Fact(Timeout = 10_000)]
    public async Task Without_a_configured_host_discovery_alone_decides_and_waits_unbounded()
    {
        var script = new Script(Attempt.Fruitless);

        await Run(preferred: null, script, TestContext.Current.CancellationToken);

        Assert.Equal(new HubAddress("192.168.178.20", 4123), script.Targets[0]);
        Assert.DoesNotContain(ConnectionLoop.PreferredHostPatience, _time.Requested);
    }

    /// <summary>
    /// A connection that worked hands back: the next round starts at the configured host again,
    /// and at one second again.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task A_connection_that_worked_starts_over_at_the_configured_host_and_one_second()
    {
        var script = new Script(Attempt.Fruitless, Attempt.Fruitless, Attempt.Worthwhile, Attempt.Fruitless);

        await Run(Preferred, script, TestContext.Current.CancellationToken);

        // Preferred, then discovery twice, then - after the one that worked - preferred again.
        Assert.Equal(Preferred, script.Targets[3]);

        var waits = _time.Requested.Where(wait => wait != ConnectionLoop.PreferredHostPatience).ToList();

        Assert.InRange(waits[1], TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(2.4));
        Assert.InRange(waits[2], TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.2));
    }

    /// <summary>
    /// A refusal waits for a person, not for the network: about five minutes, and it neither grows
    /// nor resets the ordinary count. Counted as "reached", it once reset the wait to a second, and
    /// a rejected device knocked once a second for as long as it ran.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task A_refusal_waits_minutes_and_leaves_the_growing_wait_where_it_was()
    {
        // The last outcome is never waited after - the application goes away with it.
        var script = new Script(
            Attempt.Fruitless, Attempt.Fruitless, Attempt.Refused, Attempt.Fruitless, Attempt.Fruitless);

        await Run(preferred: null, script, TestContext.Current.CancellationToken);

        var waits = _time.Requested;

        Assert.InRange(waits[0], TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.2));
        Assert.InRange(waits[1], TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(2.4));
        Assert.InRange(waits[2], TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
        Assert.InRange(waits[3], TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(4.8));
    }

    /// <summary>
    /// The bound exists so that the configured host gets its turn again: a search that hears
    /// nothing hands back to it. Without that, a display with a host - set precisely because
    /// discovery does not get through - never tried that host again after one failure, and a
    /// control restart left it searching in five-second slices for ever.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task A_search_that_hears_nothing_hands_back_to_the_configured_host()
    {
        var script = new Script(Attempt.Fruitless, Attempt.Fruitless) { Hears = false };

        await Run(Preferred, script, TestContext.Current.CancellationToken);

        Assert.Equal([Preferred, Preferred], script.Targets);
    }

    /// <summary>
    /// A search that ends without being asked to - the port could not be had, there is no network -
    /// is waited after like a failed connection. Answered with a bare "go round again", the loop
    /// spun as fast as the listener could fail.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task A_search_that_fails_by_itself_is_waited_after()
    {
        // The fourth search takes the application with it, so it is not waited after.
        var script = new Script(Attempt.Fruitless) { Hears = false, FailsAfterSearches = 4 };

        await Run(preferred: null, script, TestContext.Current.CancellationToken);

        Assert.Equal(4, script.Searches);
        Assert.Equal(3, _time.Requested.Count);
        Assert.InRange(_time.Requested[2], TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(4.8));
    }

    /// <summary>
    /// Every wait says so. The hand-runs read this line to know that the loop came round at all -
    /// without it, everything after a control restart proves nothing.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task Every_wait_is_written_down()
    {
        var script = new Script(Attempt.Fruitless, Attempt.Refused, Attempt.Fruitless);

        await Run(preferred: null, script, TestContext.Current.CancellationToken);

        Assert.Equal(_time.Requested.Count, _log.Count(1042));
    }

    /// <summary>
    /// A connection that throws is a failed attempt, not the end of the loop: written down, waited
    /// after like a control that did not answer, and tried again. Before, the fault left the loop and
    /// the display never looked for a control again until it was restarted - M1b by another door.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task A_connection_that_throws_is_waited_after_and_tried_again()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var loop = new ConnectionLoop(_log, _time, new ReconnectBackoff(seed: 7));
        var connects = 0;

        await loop.RunAsync(
            preferred: null,
            _ => Task.FromResult<Sighting?>(Heard),
            (_, _) =>
            {
                if (++connects == 1)
                {
                    throw new InvalidOperationException("a branch of the message handling failed");
                }

                stop.Cancel();

                return Task.FromResult(Attempt.Fruitless);
            },
            stop.Token);

        Assert.Equal(2, connects);
        Assert.Equal(1, _log.Count(1062));
        Assert.Equal(0, _log.Count(1013));
        Assert.InRange(_time.Requested[0], TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.2));
    }

    /// <summary>The application going away ends the loop quietly, also in the middle of a wait.</summary>
    [Fact(Timeout = 10_000)]
    public async Task Going_away_ends_the_loop_without_a_fault()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var time = new HeldTime(stop);
        var loop = new ConnectionLoop(_log, time, new ReconnectBackoff(seed: 7));

        // The first wait is where the stop comes: the clock cancels instead of letting time pass.
        await loop.RunAsync(
            preferred: null,
            _ => Task.FromResult<Sighting?>(Heard),
            (_, _) => Task.FromResult(Attempt.Fruitless),
            stop.Token);

        Assert.True(stop.IsCancellationRequested);
    }

    private async Task Run(HubAddress? preferred, Script script, CancellationToken test)
    {
        var loop = new ConnectionLoop(_log, _time, new ReconnectBackoff(seed: 7));
        using var either = CancellationTokenSource.CreateLinkedTokenSource(script.Stop.Token, test);

        await loop.RunAsync(preferred, script.Listen, script.Connect, either.Token);
    }

    /// <summary>
    /// The outcomes of successive connections, in order. When they run out, the application goes
    /// away - the only thing that ends the loop.
    /// </summary>
    private sealed class Script(params Attempt[] outcomes)
    {
        private readonly Queue<Attempt> _outcomes = new(outcomes);
        private int _listened;

        internal CancellationTokenSource Stop { get; } = new();

        internal List<HubAddress> Targets { get; } = [];

        internal int ListenedBeforeFirstConnect { get; private set; } = -1;

        /// <summary>
        /// Whether a beacon is heard. When not, a search ends the way the real listener ends it:
        /// with nothing, once its token is cancelled - by the bound, or by the application.
        /// </summary>
        internal bool Hears { get; init; } = true;

        /// <summary>
        /// After this many searches the listener fails by itself and answers at once, the way it
        /// does when the port cannot be had. The application then goes away.
        /// </summary>
        internal int? FailsAfterSearches { get; init; }

        internal int Searches => _listened;

        internal async Task<Sighting?> Listen(CancellationToken cancellationToken)
        {
            _listened++;

            if (Hears)
            {
                return Heard;
            }

            if (FailsAfterSearches is { } limit)
            {
                if (_listened >= limit)
                {
                    await Stop.CancelAsync().ConfigureAwait(false);
                }

                return null;
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            return null;
        }

        internal Task<Attempt> Connect(HubAddress target, CancellationToken cancellationToken)
        {
            if (Targets.Count == 0)
            {
                ListenedBeforeFirstConnect = _listened;
            }

            Targets.Add(target);

            var outcome = _outcomes.Dequeue();

            if (_outcomes.Count == 0)
            {
                Stop.Cancel();
            }

            return Task.FromResult(outcome);
        }
    }

    /// <summary>Records every timer it is asked for and lets it fire at once, off the caller's thread.</summary>
    private sealed class InstantTime : TimeProvider
    {
        private readonly ConcurrentQueue<TimeSpan> _requested = new();

        internal List<TimeSpan> Requested => [.. _requested];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _requested.Enqueue(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));

            return new Inert();
        }
    }

    /// <summary>A clock whose first wait never passes: it stops the application instead.</summary>
    private sealed class HeldTime(CancellationTokenSource stop) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => stop.Cancel());

            return new Inert();
        }
    }

    private sealed class Inert : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Captured : ILogger<ConnectionLoop>
    {
        private readonly ConcurrentQueue<int> _ids = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => _ids.Enqueue(eventId.Id);

        internal int Count(int eventId) => _ids.Count(id => id == eventId);
    }
}
