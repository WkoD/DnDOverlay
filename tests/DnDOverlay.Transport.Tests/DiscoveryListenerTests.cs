using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The listener over a real socket on the loopback device - which is not a shortcut but the case
/// itself: control and display on one machine are a regular setup, and discovery has to work there
/// without anybody typing an address (Part 2).
/// <para>
/// Every test that asserts WHICH control answered listens bound to its own <c>ControlId</c>, so a
/// control that happens to be running on this machine cannot make one pass or fail. That is not
/// test scaffolding either - it is exactly the filter a paired display applies. The one test that
/// listens for anybody asserts nothing about who answered, because there any answer is right.
/// </para>
/// <para>
/// The port is shared with everything else on this machine, the Hub's beacon tests included -
/// <c>dotnet test</c> runs the assemblies in parallel, and those send real datagrams to loopback
/// here. A test on this port is only sound if a stranger's beacon cannot change its verdict.
/// </para>
/// </summary>
public sealed class DiscoveryListenerTests
{
    [Fact(Timeout = 30_000)]
    public async Task A_control_that_announces_itself_is_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var control = Guid.NewGuid();
        var listener = new DiscoveryListener(NullLogger<DiscoveryListener>.Instance);

        // Bound to OUR control, and that is not scaffolding: this test asserts which beacon came
        // back, so it must not be answerable by somebody else's. It used to listen for anybody,
        // and the Hub's beacon tests - a second assembly, run in parallel by dotnet test, sending
        // real datagrams to loopback on this very port - occasionally answered it first.
        var listening = listener.ListenAsync(boundTo: control, cancellationToken);

        var sighting = await Announce(
            listening,
            new Beacon(control, "DM-SURFACE", 47801, Protocol.Version),
            cancellationToken);

        Assert.NotNull(sighting);
        Assert.Equal(control, sighting.Beacon.ControlId);
        Assert.Equal(47801, sighting.Beacon.Port);

        // The address comes from the datagram, never from what the beacon says about itself - a
        // control announcing its own idea of its address would announce the wrong one on every
        // machine with more than one interface (Part 4).
        Assert.Equal("127.0.0.1", sighting.Host);
    }

    /// <summary>
    /// The other half, and the one the case above gave up when it was bound: an UNPAIRED display
    /// takes whatever it hears first (Part 4).
    /// <para>
    /// It deliberately asserts nothing about WHICH control answered. Any beacon is a correct
    /// answer here, so a stranger on this machine cannot make it wrong - which is exactly what the
    /// bound case could not say of itself.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task An_unpaired_display_takes_the_first_control_it_hears()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var listener = new DiscoveryListener(NullLogger<DiscoveryListener>.Instance);

        var listening = listener.ListenAsync(boundTo: null, cancellationToken);

        var sighting = await Announce(
            listening,
            new Beacon(Guid.NewGuid(), "DM-SURFACE", 47802, Protocol.Version),
            cancellationToken);

        Assert.NotNull(sighting);
        Assert.Equal("127.0.0.1", sighting.Host);
    }

    /// <summary>
    /// A paired display belongs to ITS control. The address is no good for telling controls apart
    /// - it changes - and a second control in the same network is no invention (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_paired_display_ignores_a_foreign_control()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mine = Guid.NewGuid();
        var listener = new DiscoveryListener(NullLogger<DiscoveryListener>.Instance);

        var listening = listener.ListenAsync(boundTo: mine, cancellationToken);

        // The stranger first, then the right one: the listener has to walk past the first without
        // taking it and without giving up.
        var sighting = await Announce(
            listening,
            new Beacon(Guid.NewGuid(), "SOMEBODY-ELSE", 47800, Protocol.Version),
            new Beacon(mine, "DM-SURFACE", 47800, Protocol.Version),
            cancellationToken);

        Assert.NotNull(sighting);
        Assert.Equal(mine, sighting.Beacon.ControlId);
        Assert.Equal("DM-SURFACE", sighting.Beacon.Name);
    }

    /// <summary>
    /// Noise on that port is the normal state of a home network, and it must not end the search:
    /// a listener that gave up on the first stray datagram would be a listener that never finds
    /// anything.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Noise_on_the_port_does_not_end_the_search()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var control = Guid.NewGuid();
        var listener = new DiscoveryListener(NullLogger<DiscoveryListener>.Instance);

        var listening = listener.ListenAsync(boundTo: control, cancellationToken);

        using (var noisy = Sender())
        {
            for (var i = 0; i < 5; i++)
            {
                var rubbish = "not a beacon at all"u8.ToArray();

                await noisy.SendAsync(rubbish, Target(), cancellationToken);
            }
        }

        var sighting = await Announce(
            listening,
            new Beacon(control, "DM-SURFACE", 47800, Protocol.Version),
            cancellationToken);

        Assert.NotNull(sighting);
        Assert.Equal(control, sighting.Beacon.ControlId);
    }

    /// <summary>
    /// The counterpart to the test above, and it exists because of a dead end a hand run found: a
    /// control whose <c>control.json</c> was replaced comes back with a NEW identifier, so its own
    /// displays discard it here — no <c>Hello</c>, no rejection, no entry anywhere. The filter
    /// stays as it is (loosening it is the attack it prevents); what has to hold is that it can be
    /// READ.
    /// <para>
    /// So: the first sighting of each strange control is named at Information, every one after it
    /// falls back to Debug. Both halves matter — without the first the dead end is silent, and
    /// without the second a household with two controls writes a line every two seconds.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_strange_control_is_named_once_and_after_that_kept_quiet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mine = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var log = new Captured();
        var listener = new DiscoveryListener(log);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var listening = listener.ListenAsync(boundTo: mine, stop.Token);

        using (var sender = Sender())
        {
            var datagram = DiscoveryJson.Serialise(new Beacon(stranger, "SOMEBODY-ELSE", 47800, Protocol.Version));

            // Until it has been named once: the socket may not be bound when the first datagram
            // goes out. Repeating is what a beacon does anyway, every two seconds.
            while (log.Count(1048, stranger) == 0)
            {
                await sender.SendAsync(datagram, Target(), cancellationToken);
                await Task.Delay(25, cancellationToken);
            }

            // And now the repeats, which must not name it a second time.
            for (var i = 0; i < 5; i++)
            {
                await sender.SendAsync(datagram, Target(), cancellationToken);
                await Task.Delay(25, cancellationToken);
            }
        }

        await stop.CancelAsync();

        Assert.Null(await listening);

        // Counted over OUR stranger alone. This port carries the beacons of every other test
        // assembly running in parallel, so a count over all entries would be a coin toss.
        Assert.Equal(1, log.Count(1048, stranger));
        Assert.True(log.Count(1017, stranger) > 0, "the repeats have to be written, only at Debug");
    }

    [Fact(Timeout = 30_000)]
    public async Task Giving_up_the_search_answers_with_nothing()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var listener = new DiscoveryListener(NullLogger<DiscoveryListener>.Instance);

        var listening = listener.ListenAsync(boundTo: null, stop.Token);

        await stop.CancelAsync();

        Assert.Null(await listening);
    }

    private static Task<Sighting?> Announce(
        Task<Sighting?> listening,
        Beacon beacon,
        CancellationToken cancellationToken) =>
        Announce(listening, beacon, second: null, cancellationToken);

    /// <summary>
    /// Sends until the listener has taken one. A single datagram would be a race with a socket
    /// that may not be bound yet - repeating is what a beacon does anyway, every two seconds.
    /// </summary>
    private static async Task<Sighting?> Announce(
        Task<Sighting?> listening,
        Beacon beacon,
        Beacon? second,
        CancellationToken cancellationToken)
    {
        using var sender = Sender();

        while (!listening.IsCompleted)
        {
            var datagram = DiscoveryJson.Serialise(beacon);

            await sender.SendAsync(datagram, Target(), cancellationToken);

            if (second is not null)
            {
                var other = DiscoveryJson.Serialise(second);

                await sender.SendAsync(other, Target(), cancellationToken);
            }

            await Task.WhenAny(listening, Task.Delay(50, cancellationToken));
        }

        return await listening;
    }

    private static UdpClient Sender()
    {
        var sender = new UdpClient();

        sender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        return sender;
    }

    private static IPEndPoint Target() => new(IPAddress.Loopback, Protocol.DiscoveryPort);

    /// <summary>
    /// Keeps what was written, so a test can assert about the LEVEL a thing was said at rather
    /// than only about what happened. Thread-safe because the listener writes from its own task.
    /// </summary>
    private sealed class Captured : ILogger<DiscoveryListener>
    {
        private readonly ConcurrentQueue<(int Id, string Message)> _entries = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _entries.Enqueue((eventId.Id, formatter(state, exception)));
        }

        internal int Count(int eventId, Guid mentioning) =>
            _entries.Count(entry =>
                entry.Id == eventId
                && entry.Message.Contains(mentioning.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
