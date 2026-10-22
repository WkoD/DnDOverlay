using System.Threading.Channels;
using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// The device's log entries travel to the control over the connection that is already there, and
/// the mark that says what went out lives in the forwarder, not in the connection (Part 8).
/// </summary>
public sealed class LogForwardingTests
{
    private static readonly LogIdentity Identity = new("DnDOverlay.Display", "0.1.0-test", 1);
    private static readonly ScreenId Table = new("table");

    /// <summary>What came up while nothing was connected goes out the moment a connection stands.</summary>
    [Fact]
    public async Task What_came_up_before_the_connection_goes_out_at_once()
    {
        using var log = Log();
        var forwarding = new LogForwarding(log, LogLevel.Warning);

        log.Add(Record(3005, LogLevel.Warning, Table));

        var outbox = new Outbox();
        var sent = await RunUntil(forwarding, outbox, count: 1);

        var entry = Assert.Single(sent);
        Assert.Equal(3005, entry.EventId);
        Assert.Equal("Event3005", entry.EventName);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(Table, entry.Screen);
        Assert.Equal("Dilwyn", Assert.Single(entry.Values).Text);
    }

    /// <summary>The file keeps everything; the wire carries what is worth the DM's attention (Part 6).</summary>
    [Fact]
    public async Task Only_what_reaches_the_forwarding_level_travels()
    {
        using var log = Log();
        var forwarding = new LogForwarding(log, LogLevel.Warning);

        log.Add(Record(1, LogLevel.Information));
        log.Add(Record(2, LogLevel.Warning));
        log.Add(Record(3, LogLevel.Debug));
        log.Add(Record(4, LogLevel.Error));

        var sent = await RunUntil(forwarding, new Outbox(), count: 2);

        Assert.Equal([2, 4], sent.Select(entry => entry.EventId));
    }

    /// <summary>
    /// A second connection sends only what the first did not: the mark survives the connection, so
    /// nothing goes twice and nothing that came up in between is lost.
    /// </summary>
    [Fact]
    public async Task The_next_connection_carries_on_where_the_last_one_stopped()
    {
        using var log = Log();
        var forwarding = new LogForwarding(log, LogLevel.Warning);

        log.Add(Record(1, LogLevel.Warning));
        log.Add(Record(2, LogLevel.Warning));

        _ = await RunUntil(forwarding, new Outbox(), count: 2);

        // Between two connections - the most interesting lines come up exactly then.
        log.Add(Record(3, LogLevel.Error));

        var second = await RunUntil(forwarding, new Outbox(), count: 1);

        Assert.Equal([3], second.Select(entry => entry.EventId));
    }

    /// <summary>An entry written while the connection stands is sent without waiting for anything else.</summary>
    [Fact]
    public async Task An_entry_during_the_connection_wakes_the_forwarder()
    {
        using var log = Log();
        var forwarding = new LogForwarding(log, LogLevel.Warning);
        var outbox = new Outbox();
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = forwarding.RunAsync(outbox, connection.Token);

        log.Add(Record(7, LogLevel.Warning));

        var entry = await outbox.Sent.Reader.ReadAsync(TestContext.Current.CancellationToken);

        await connection.CancelAsync();
        await running;

        Assert.Equal(7, entry.EventId);
    }

    /// <summary>
    /// A full state queue means the connection is closing: the forwarder stops by itself, and the mark
    /// stays where it was, so the next connection sends what this one could not (Part 4, Part 8).
    /// </summary>
    [Fact]
    public async Task A_refused_entry_ends_the_pass_and_stays_for_the_next_connection()
    {
        using var log = Log();
        var forwarding = new LogForwarding(log, LogLevel.Warning);

        log.Add(Record(1, LogLevel.Warning));
        log.Add(Record(2, LogLevel.Warning));

        var refusing = new Outbox(accept: 0);

        // Ends on its own, without the connection being cancelled.
        await forwarding.RunAsync(refusing, TestContext.Current.CancellationToken);

        Assert.Equal(1, refusing.Offered);

        var next = await RunUntil(forwarding, new Outbox(), count: 2);

        Assert.Equal([1, 2], next.Select(entry => entry.EventId));
    }

    /// <summary>
    /// The level is read on every pass rather than captured: lowering it from the control takes effect
    /// on the connection that stands (Part 6).
    /// </summary>
    [Fact]
    public async Task A_changed_level_takes_effect_without_a_new_connection()
    {
        using var log = Log();
        var forwarding = new LogForwarding(log, LogLevel.Warning);
        var outbox = new Outbox();
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = forwarding.RunAsync(outbox, connection.Token);

        forwarding.AtLeast = LogLevel.Information;
        log.Add(Record(9, LogLevel.Information));

        var entry = await outbox.Sent.Reader.ReadAsync(TestContext.Current.CancellationToken);

        await connection.CancelAsync();
        await running;

        Assert.Equal(9, entry.EventId);
    }

    private static ProcessLog Log() =>
        new(Identity, directory: null, LogFileLimits.Display, TimeProvider.System) { Level = LogLevel.Trace };

    private static LogRecord Record(int eventId, LogLevel level, ScreenId? screen = null) =>
        new(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            level,
            eventId,
            $"Event{eventId}",
            [new LogValue("Name", "Dilwyn")],
            Screen: screen);

    /// <summary>Runs one connection until the outbox has taken this many entries, then ends it.</summary>
    private static async Task<List<LogEntryMessage>> RunUntil(LogForwarding forwarding, Outbox outbox, int count)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = forwarding.RunAsync(outbox, connection.Token);
        var sent = new List<LogEntryMessage>();

        while (sent.Count < count)
        {
            sent.Add(await outbox.Sent.Reader.ReadAsync(TestContext.Current.CancellationToken));
        }

        await connection.CancelAsync();
        await running;

        // Whatever arrived after the count is part of what this connection sent too.
        while (outbox.Sent.Reader.TryRead(out var more))
        {
            sent.Add(more);
        }

        return sent;
    }

    /// <summary>The connection's queue: takes a number of entries and refuses the rest.</summary>
    private sealed class Outbox(int accept = int.MaxValue) : IMessageSink
    {
        private int _offered;

        internal Channel<LogEntryMessage> Sent { get; } = Channel.CreateUnbounded<LogEntryMessage>();

        internal int Offered => Volatile.Read(ref _offered);

        public bool TrySend(ProtocolMessage message)
        {
            if (Interlocked.Increment(ref _offered) > accept)
            {
                return false;
            }

            return Sent.Writer.TryWrite((LogEntryMessage)message);
        }
    }
}
