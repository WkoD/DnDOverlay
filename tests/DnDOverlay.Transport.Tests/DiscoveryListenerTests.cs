using System.Net;
using System.Net.Sockets;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The listener over a real socket on the loopback device - which is not a shortcut but the case
/// itself: control and display on one machine are a regular setup, and discovery has to work there
/// without anybody typing an address (Part 2).
/// <para>
/// Every test uses its own <c>ControlId</c>, so a control that happens to be running on this
/// machine cannot make one pass or fail. That is not test scaffolding either - it is exactly the
/// filter a paired display applies.
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

        var listening = listener.ListenAsync(boundTo: null, cancellationToken);

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

    [Fact(Timeout = 30_000)]
    public async Task Giving_up_the_search_answers_with_nothing()
    {
        using var stop = new CancellationTokenSource();
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
}
