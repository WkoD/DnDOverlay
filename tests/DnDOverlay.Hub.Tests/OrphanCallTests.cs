using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The call for orphaned displays: two minutes in the beacon, and only when somebody asked for it
/// (Part 4).
/// </summary>
public sealed class OrphanCallTests
{
    /// <summary>
    /// Nothing calls on its own. A pure time rule would offer the displays of one control to a
    /// second, legitimate one - the press is the point.
    /// </summary>
    [Fact]
    public void Nobody_is_called_until_somebody_asks()
    {
        var clock = new Clock();
        var call = new OrphanCall(clock);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.False(call.Calling);
    }

    [Fact]
    public void The_call_lasts_two_minutes_and_then_stops()
    {
        var clock = new Clock();
        var call = new OrphanCall(clock);

        call.Start();
        clock.Advance(OrphanCall.Duration - TimeSpan.FromSeconds(1));

        Assert.True(call.Calling);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(call.Calling);
    }

    /// <summary>A second press starts the two minutes again rather than adding to them or being ignored.</summary>
    [Fact]
    public void Asking_again_starts_the_two_minutes_from_now()
    {
        var clock = new Clock();
        var call = new OrphanCall(clock);

        call.Start();
        clock.Advance(TimeSpan.FromMinutes(1.5));
        call.Start();
        clock.Advance(TimeSpan.FromMinutes(1.5));

        Assert.True(call.Calling);
    }

    /// <summary>The command is the only way to set it, and it reaches the same call the beacon reads.</summary>
    [Fact]
    public async Task The_command_sets_the_call()
    {
        var call = new OrphanCall(new Clock());
        var session = new SessionApi(
            new SceneStore(),
            new ScreenCatalog(),
            new DisplayConnections(),
            new PairingDirectory(Options.Create(new HubOptions()), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance,
            call);

        await session.CallOrphansAsync(TestContext.Current.CancellationToken);

        Assert.True(call.Calling);
    }

    /// <summary>
    /// The beacon is built every round, so a call started while it runs is in the next datagram -
    /// over a real socket, filtered by our own identifier like every test on this port.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task The_beacon_carries_the_call_while_it_lasts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var control = Guid.NewGuid();
        var call = new OrphanCall(TimeProvider.System);

        using var ear = new System.Net.Sockets.UdpClient();
        ear.Client.SetSocketOption(
            System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
        ear.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, Protocol.DiscoveryPort));

        var beacon = new DiscoveryBeacon(
            Options.Create(new HubOptions { ControlId = control, Port = 47812 }),
            TimeProvider.System,
            NullLogger<DiscoveryBeacon>.Instance,
            call);

        await beacon.StartAsync(cancellationToken);

        try
        {
            Assert.False((await Heard(ear, control, cancellationToken)).CallingOrphans);

            call.Start();

            // The next round may still have been built before the press; the one after cannot.
            while (!(await Heard(ear, control, cancellationToken)).CallingOrphans)
            {
            }
        }
        finally
        {
            await beacon.StopAsync(cancellationToken);
        }
    }

    private static async Task<Beacon> Heard(System.Net.Sockets.UdpClient ear, Guid control, CancellationToken cancellationToken)
    {
        while (true)
        {
            var datagram = await ear.ReceiveAsync(cancellationToken);
            var heard = DiscoveryJson.Parse(datagram.Buffer);

            if (heard?.ControlId == control)
            {
                return heard;
            }
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        internal void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
