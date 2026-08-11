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
        Assert.IsType<SceneSnapshotMessage>(await first.NextAsync(cancellationToken));
        Assert.Empty(hub.Session.PendingPairings);

        // And back again with what it was given - the normal case at every power-on.
        await using var again = await hub.ConnectAsync(Hello(token: Token), answersPing: false, cancellationToken);

        var second = Assert.IsType<WelcomeMessage>(await again.NextAsync(cancellationToken));

        Assert.Null(second.Token);
        Assert.IsType<SceneSnapshotMessage>(await again.NextAsync(cancellationToken));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_token_this_control_does_not_know_ends_the_connection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = await Hub.StartAsync(cancellationToken, Paired());
        await using var display = await hub.ConnectAsync(Hello(token: "guessed"), answersPing: false, cancellationToken);

        var rejected = Assert.IsType<RejectedMessage>(await display.NextAsync(cancellationToken));

        Assert.Equal(RejectionReason.InvalidToken, rejected.Reason);
        await display.WaitUntilClosedAsync(cancellationToken);

        // It stays visible with its reason instead of simply disappearing (Part 4, Part 7).
        Assert.Equal(RejectionReason.InvalidToken, Assert.Single(hub.Session.RefusedDevices).Reason);
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
        await using var hub = await Hub.StartAsync(cancellationToken, Paired());

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
        await using var hub = await Hub.StartAsync(cancellationToken, Paired());

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
            token is null ? "4271" : null);

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

        internal static async Task<Hub> StartAsync(CancellationToken cancellationToken, params PairedDevice[] known)
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
                hub.KnownDevices = known;

                // Shorter than the second of Part 4, and it changes nothing about what is being
                // proven: the silent side never answers, so waiting longer could only make the
                // test slower, never different.
                hub.CloneProbe = TimeSpan.FromMilliseconds(200);
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

        private Task _pump = Task.CompletedTask;

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

                    if (message is PingMessage && answersPing)
                    {
                        await SendAsync(new PongMessage(), _stop.Token);
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
