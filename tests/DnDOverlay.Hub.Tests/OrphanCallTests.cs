using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.DependencyInjection;
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
    /// DM decision of 22.10.2026: with "accept new devices" off, the call opens the door for its two
    /// minutes to the displays answering it - they bring the token of the control they were bound to -
    /// and to nobody else: a new device, which brings none, stays outside, and so does an answer that
    /// comes after the two minutes. The press is the consent; the DM still decides each request.
    /// </summary>
    [Fact]
    public void While_it_lasts_the_call_lets_its_answers_past_a_closed_gate_and_nobody_else()
    {
        var clock = new Clock();
        var call = new OrphanCall(clock);
        var directory = new PairingDirectory(Options.Create(new HubOptions { AcceptNewDevices = false }), TimeProvider.System, call);

        Assert.IsType<Admission.Refused>(directory.Consider(Hello(1, token: "of-the-old-control"), "10.0.0.7"));

        call.Start();

        var answer = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(2, token: "of-the-old-control"), "10.0.0.8"));

        Assert.True(answer.Request.Snapshot.BroughtUnknownToken);
        Assert.False(answer.Request.Decision.IsCompleted);
        Assert.IsType<Admission.Refused>(directory.Consider(Hello(3), "10.0.0.9"));

        clock.Advance(OrphanCall.Duration);

        Assert.IsType<Admission.Refused>(directory.Consider(Hello(4, token: "of-the-old-control"), "10.0.0.10"));
        Assert.Equal(new DeviceId(Some(2)), Assert.Single(directory.Pending).Device);
        Assert.False(directory.AcceptNewDevices);
    }

    /// <summary>The hub hands the call it carries in the beacon to the pairing, so the press reaches the gate.</summary>
    [Fact]
    public void The_hub_hands_its_call_to_the_pairing()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDnDOverlayHub(options => options.AcceptNewDevices = false);

        using var provider = services.BuildServiceProvider();

        var directory = provider.GetRequiredService<PairingDirectory>();

        provider.GetRequiredService<OrphanCall>().Start();

        Assert.IsType<Admission.Waiting>(directory.Consider(Hello(2, token: "of-the-old-control"), "10.0.0.8"));
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

    private static Guid Some(int number) => new($"00000000-0000-0000-0000-0000000000{number:00}");

    private static HelloMessage Hello(int device, string? token = null) =>
        new(new DeviceId(Some(device)),
            "TISCH-PC",
            "1.0.0",
            Protocol.Version,
            [new ScreenInfo(new ScreenId(@"\\?\DISPLAY#TEST#1"), "TISCH-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, true)],
            token,
            "4271");

    private sealed class Clock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        internal void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
