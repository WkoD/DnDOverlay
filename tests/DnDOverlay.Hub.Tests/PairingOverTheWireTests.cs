using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The half of pairing that only exists over a socket: that a refusal really ends the connection,
/// that a fresh token travels exactly once, and that a clone and a crashed display coming straight
/// back are told apart by an ANSWER rather than by a deadline (Part 4).
/// <para>
/// Against a real Kestrel, like the running thread next door. Each test starts its own hub, so
/// what a test begins with is stated in the test rather than inherited from a fixture.
/// </para>
/// </summary>
public sealed class PairingOverTheWireTests
{
    private const string Token = "a-token-that-was-issued-earlier";

    private static readonly DeviceId Device = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"));

    [Fact(Timeout = 30_000)]
    public async Task An_unpaired_device_is_told_that_the_request_is_with_the_DM()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken);
        await using var display = await hub.ConnectAsync(Hello(), answersPing: false, cancellationToken);

        var pending = Assert.IsType<PairingPendingMessage>(await display.NextAsync(cancellationToken));

        Assert.Equal("4271", pending.PairingCode);

        var request = Assert.Single(hub.Session.PendingPairings);

        Assert.Equal("TISCH-PC", request.Name);
        Assert.Equal("4271", request.PairingCode);
    }

    /// <summary>
    /// The token travels exactly once: in the answer to the pairing the DM just allowed. On every
    /// later connection the display brings its own and the <c>Welcome</c> carries none.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Allowing_a_device_hands_it_a_token_it_can_come_back_with()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken);
        await using var first = await hub.ConnectAsync(Hello(), answersPing: false, cancellationToken);

        Assert.IsType<PairingPendingMessage>(await first.NextAsync(cancellationToken));

        await hub.Session.ApprovePairingAsync(Device, Token, cancellationToken: cancellationToken);

        var welcome = Assert.IsType<WelcomeMessage>(await first.NextAsync(cancellationToken));

        Assert.Equal(Token, welcome.Token);
        await first.NextAsync<SceneSnapshotMessage>(cancellationToken);
        Assert.Empty(hub.Session.PendingPairings);

        // And back again with what it was given - the normal case at every power-on.
        await using var again = await hub.ConnectAsync(Hello(token: Token), answersPing: false, cancellationToken);

        var second = Assert.IsType<WelcomeMessage>(await again.NextAsync(cancellationToken));

        Assert.Null(second.Token);
        await again.NextAsync<SceneSnapshotMessage>(cancellationToken);
    }

    /// <summary>
    /// The whole way through, over a real socket: a token this control does not know keeps the
    /// connection OPEN and waits, instead of ending it. That is the case a replaced
    /// <c>control.json</c> produces on every display at once, and ending the connection there sent
    /// the DM to every machine in the flat (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_token_this_control_does_not_know_waits_for_the_DM_instead_of_ending_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken, [Paired()]);
        await using var display = await hub.ConnectAsync(Hello(token: "guessed"), answersPing: false, cancellationToken);

        Assert.IsType<PairingPendingMessage>(await display.NextAsync(cancellationToken));

        var pending = Assert.Single(hub.Session.PendingPairings);

        Assert.True(pending.BroughtUnknownToken);
        Assert.Empty(hub.Session.RefusedDevices);

        // Allowed at the control, and the device is in - without anybody having gone anywhere.
        await hub.Session.ApprovePairingAsync(pending.Device, "fresh-token", PairingRole.Display, cancellationToken);

        var welcome = Assert.IsType<WelcomeMessage>(await display.NextAsync(cancellationToken));

        Assert.Equal("fresh-token", welcome.Token);
    }

    /// <summary>
    /// Nothing expires; the request stands as long as the connection stands. What is in the list
    /// is therefore always what is knocking right now (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_device_that_goes_away_takes_its_request_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken);

        var display = await hub.ConnectAsync(Hello(), answersPing: false, cancellationToken);

        Assert.IsType<PairingPendingMessage>(await display.NextAsync(cancellationToken));
        Assert.Single(hub.Session.PendingPairings);

        await display.DisposeAsync();

        await Until(() => hub.Session.PendingPairings.Count == 0, cancellationToken);
        Assert.Empty(hub.Session.RefusedDevices);
    }

    /// <summary>
    /// A crashed display that comes straight back looks exactly like a clone. Silence to the probe
    /// says it was the same machine, and the old connection is replaced (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_silent_connection_makes_way_for_the_one_that_came_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken, [Paired()]);

        var crashed = await hub.ConnectAsync(Hello(token: Token), answersPing: false, cancellationToken);
        Assert.IsType<WelcomeMessage>(await crashed.NextAsync(cancellationToken));

        await using var restarted = await hub.ConnectAsync(Hello(token: Token), answersPing: false, cancellationToken);

        Assert.IsType<WelcomeMessage>(await restarted.NextAsync(cancellationToken));
        await crashed.WaitUntilClosedAsync(cancellationToken);

        // The registry lets go too, and by instance: the newcomer is still there.
        Assert.Equal(1, hub.Connections.Count);

        await crashed.DisposeAsync();
    }

    /// <summary>
    /// The same situation with the opposite answer: the connection that is already there speaks
    /// up, so there really are two machines - and the DM decides instead of one of them being
    /// turned away. Cloning a disk is the usual way to set up a second display PC (Part 7).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_clone_is_laid_in_front_of_the_DM_and_told_to_take_a_fresh_identity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken, [Paired()]);

        await using var original = await hub.ConnectAsync(Hello(token: Token), answersPing: true, cancellationToken);
        Assert.IsType<WelcomeMessage>(await original.NextAsync(cancellationToken));

        await using var clone = await hub.ConnectAsync(Hello(token: Token), answersPing: false, cancellationToken);

        Assert.IsType<PairingPendingMessage>(await clone.NextAsync(cancellationToken));
        Assert.True(Assert.Single(hub.Session.PendingPairings).IsClone);
        Assert.False(original.Closed);

        await hub.Session.AcceptAsOwnDeviceAsync(Device, cancellationToken);

        var rejected = Assert.IsType<RejectedMessage>(await clone.NextAsync(cancellationToken));

        Assert.Equal(RejectionReason.DuplicateDevice, rejected.Reason);
        Assert.False(original.Closed);
    }


    /// <summary>
    /// The heartbeat covers a connection that is still waiting for the DM, and it has to: an open
    /// request stands as long as its CONNECTION stands (Part 4). Without a beat there, a display
    /// PC that was switched off mid-request would sit in the device list as something that is
    /// knocking - and TCP alone would not notice for hours.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_waiting_device_that_goes_quiet_takes_its_request_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken, tune: Impatient);
        await using var display = await hub.ConnectAsync(Hello(), answersPing: false, cancellationToken);

        Assert.IsType<PairingPendingMessage>(await display.NextAsync(cancellationToken));
        Assert.Single(hub.Session.PendingPairings);

        // Nothing is unplugged and nothing is closed at this end: the device is simply mute, which
        // is exactly what a machine that has been switched off looks like from here.
        await display.WaitUntilClosedAsync(cancellationToken);
        await Until(() => hub.Session.PendingPairings.Count == 0, cancellationToken);

        Assert.Empty(hub.Session.RefusedDevices);
    }

    /// <summary>
    /// The other direction, and the one that costs more when it is wrong: a device that answers
    /// stays, however many silence windows pass. A heartbeat that drops healthy connections would
    /// interrupt the session in front of the group and look like a defect (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_device_that_answers_stays_and_is_told_its_round_trip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken, [Paired()], Impatient);
        await using var display = await hub.ConnectAsync(Hello(token: Token), answersPing: true, cancellationToken);

        Assert.IsType<WelcomeMessage>(await display.NextAsync(cancellationToken));

        // Several beats and several silence windows later it is still there - and the ping brings
        // back the number the control measured, so both sides show the same latency rather than
        // each working one out its own way.
        await Until(() => display.Pings.Any(ping => ping.RoundTripMs is not null), cancellationToken);

        Assert.False(display.Closed);
        Assert.Equal(1, hub.Connections.Count);
    }

    /// <summary>
    /// A beat and a silence window measured in milliseconds rather than seconds. It changes
    /// nothing about what is being proven - only how long the proof takes.
    /// <para>
    /// <b>The silence window has a floor, and 200 ms was below it.</b> The clock starts when the
    /// socket is ACCEPTED, the pairing wait included - deliberately, because that is what makes
    /// "what is in the list is knocking right now" true (see <c>Liveness</c>). So everything the
    /// client still has to do afterwards - finish the handshake, serialise, send the Hello - falls
    /// inside the window, and on a loaded CI runner that overran 200 ms: the hub declared the
    /// connection dead before the Hello arrived, and the test failed while SENDING it.
    /// </para>
    /// <para>
    /// A second is therefore a margin against machine load, not a property under test. It costs
    /// one test about a second and none of them anything else - the round-trip test waits on the
    /// beat, not on this.
    /// </para>
    /// </summary>
    private static void Impatient(HubOptions hub)
    {
        hub.HeartbeatInterval = TimeSpan.FromMilliseconds(200);
        hub.SilenceBeforeDead = TimeSpan.FromSeconds(1);
    }

    private static PairedDevice Paired() => new(Device, "TISCH-PC", PairingRole.Display, Token);

    /// <summary>There is no stock here; these tests are about who may connect, not about images.</summary>
    private sealed class NoAssets : IAssetSource
    {
        public bool TryOpen(AssetId id, out Stream data, out string contentType)
        {
            data = Stream.Null;
            contentType = string.Empty;

            return false;
        }
    }

    private static HelloMessage Hello(string? token = null) =>
        new(Device,
            "TISCH-PC",
            "1.0.0",
            Protocol.Version,
            [new ScreenInfo(new ScreenId(@"\\?\DISPLAY#TEST#1"), "TISCH-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, true)],
            token,

            // Always, token or not - see the directory tests: an unknown token is a request now,
            // and a request without a code cannot be compared with the table (Part 4).
            "4271");

    /// <summary>
    /// Polls for a state the hub reaches on its own schedule. A fixed sleep would be either flaky
    /// or slow; this is neither, and the test's own timeout is the ceiling.
    /// </summary>
    private static async Task Until(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    /// <summary>One hub with its own port, started per test.</summary>
    private sealed class Hub : IAsyncDisposable
    {
        private WebApplication _app = null!;

        internal ISessionApi Session { get; private set; } = null!;

        internal DisplayConnections Connections => _app.Services.GetRequiredService<DisplayConnections>();

        private Uri Address { get; set; } = null!;

        internal static async Task<Hub> StartAsync(
            CancellationToken cancellationToken,
            IReadOnlyList<PairedDevice>? known = null,
            Action<HubOptions>? tune = null)
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            // Registered although not one of these tests fetches an asset. Minimal APIs build
            // ALL endpoints of a source at the first request, so an unresolvable parameter on
            // /assets/{id} answers /ws/display with a 500 as well - one endpoint nobody calls
            // takes the others down with it.
            builder.Services.AddSingleton<IAssetSource>(new NoAssets());

            builder.Services.AddDnDOverlayHub(hub =>
            {
                hub.KnownDevices = known ?? [];

                // Shorter than the second of Part 4, and it changes nothing about what is being
                // proven: the silent side never answers, so waiting longer could only make the
                // test slower, never different.
                hub.CloneProbe = TimeSpan.FromMilliseconds(200);

                tune?.Invoke(hub);
            });

            var app = builder.Build();
            app.UseWebSockets();
            app.MapDnDOverlayHub();

            await app.StartAsync(cancellationToken);

            var address = new Uri(app.Urls.First());

            return new Hub
            {
                _app = app,
                Session = app.Services.GetRequiredService<ISessionApi>(),
                Address = new Uri($"ws://{address.Authority}{Protocol.DisplayPath}"),
            };
        }

        internal async Task<Peer> ConnectAsync(HelloMessage hello, bool answersPing, CancellationToken cancellationToken) =>
            await Peer.ConnectAsync(Address, hello, answersPing, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// A display as far as the hub can tell: it says <c>Hello</c>, reads what comes, and - if it
    /// is meant to be alive - answers <c>Ping</c>. Answering is what makes it a clone rather than
    /// a corpse, so it is the one behaviour these tests switch on and off.
    /// </summary>
    private sealed class Peer : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly Channel<ProtocolMessage> _inbox = Channel.CreateUnbounded<ProtocolMessage>();
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ConcurrentQueue<PingMessage> _pings = new();

        private Task _pump = Task.CompletedTask;

        /// <summary>The heartbeat as the device sees it, so a test can wait for a beat.</summary>
        internal IReadOnlyCollection<PingMessage> Pings => _pings;

        /// <summary>
        /// Whether the connection has ended - taken from the pump rather than from
        /// <c>Reader.Completion</c>. That one only completes once every queued message has been
        /// READ as well, so a test that ignores a snapshot it does not care about would wait for
        /// a close that already happened. It cost a measurement to find, and it was the test that
        /// was wrong, not the hub.
        /// </summary>
        internal bool Closed => _closed.Task.IsCompleted;

        internal static async Task<Peer> ConnectAsync(
            Uri address,
            HelloMessage hello,
            bool answersPing,
            CancellationToken cancellationToken)
        {
            var peer = new Peer();

            await peer._socket.ConnectAsync(address, cancellationToken);
            await peer.SendAsync(hello, cancellationToken);

            peer._pump = Task.Run(() => peer.PumpAsync(answersPing), CancellationToken.None);

            return peer;
        }

        internal async Task<ProtocolMessage> NextAsync(CancellationToken cancellationToken) =>
            await _inbox.Reader.ReadAsync(cancellationToken);

        /// <summary>
        /// Reads until the message this test is about arrives, passing over whatever else the hub
        /// has to say first. Waiting for a POSITION in the queue would tie every test here to how
        /// many things a connecting display is told - a number that grows with the milestones.
        /// </summary>
        internal async Task<T> NextAsync<T>(CancellationToken cancellationToken)
            where T : ProtocolMessage
        {
            while (await _inbox.Reader.ReadAsync(cancellationToken) is { } message)
            {
                if (message is T wanted)
                {
                    return wanted;
                }
            }

            throw new InvalidOperationException($"The connection ended before a {typeof(T).Name} arrived.");
        }

        internal async Task WaitUntilClosedAsync(CancellationToken cancellationToken) =>
            await _closed.Task.WaitAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
                // Expected: that is how the pump is stopped.
            }

            _socket.Dispose();
            _stop.Dispose();
        }

        private async Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken) =>
            await _socket.SendAsync(
                ProtocolJson.Serialise(message),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);

        private async Task PumpAsync(bool answersPing)
        {
            var buffer = new byte[64 * 1024];

            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(buffer.AsMemory(), _stop.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    var message = ProtocolJson.Parse(buffer.AsSpan(0, result.Count));

                    if (message is PingMessage ping)
                    {
                        _pings.Enqueue(ping);

                        if (answersPing)
                        {
                            await SendAsync(new PongMessage(), _stop.Token);
                        }

                        continue;
                    }

                    if (message is not null)
                    {
                        await _inbox.Writer.WriteAsync(message, _stop.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stopping.
            }
            catch (WebSocketException)
            {
                // The hub closed this connection - which is a result, not a fault.
            }
            finally
            {
                _inbox.Writer.TryComplete();
                _closed.TrySetResult();
            }
        }
    }
}
