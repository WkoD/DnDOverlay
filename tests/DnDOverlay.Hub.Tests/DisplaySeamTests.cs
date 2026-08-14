using System.Threading.Channels;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The real <see cref="DisplayClient"/> against the real hub - the second seam, and the one on the
/// more important wire.
/// <para>
/// It had no test of any kind. The hub's socket is checked with a hand-built
/// <c>ClientWebSocket</c> and hand-written JSON; the client the display actually runs was checked
/// nowhere. That is the same shape that had already cost the asset fetch a whole commit, and here
/// it sits on the connection everything else hangs from.
/// </para>
/// <para>
/// What is checked is that the two AGREE - handshake, framing, message types, both directions.
/// Each half was already proved against its own counterpart, which is a stand-in that agrees with
/// whoever wrote it.
/// </para>
/// </summary>
public sealed class DisplaySeamTests : IAsyncLifetime
{
    private const string Token = "the-display-token";

    /// <summary>
    /// A fast heartbeat and a short deadline, so the send loop can be shown to work in seconds
    /// rather than in the twelve the real value would need.
    /// </summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);

    private static readonly DeviceId Device = new(Guid.Parse("dddddddd-0000-0000-0000-000000000001"));
    private static readonly ScreenId Screen = new("SEAM//DISPLAY1");

    private WebApplication _app = null!;
    private Uri _ws = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDnDOverlayHub(hub =>
        {
            hub.KnownDevices = [new PairedDevice(Device, "SEAM", PairingRole.Display, Token)];
            hub.HeartbeatInterval = Heartbeat;
            hub.SilenceBeforeDead = Silence;
        });
        builder.Services.AddSingleton<IAssetSource>(new NoAssets());

        _app = builder.Build();
        _app.UseWebSockets();
        _app.MapDnDOverlayHub();

        await _app.StartAsync();

        var address = new Uri(_app.Urls.First());
        _ws = new Uri($"ws://{address.Authority}{Protocol.DisplayPath}");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// The handshake over the wire with both real halves: the client's <c>Hello</c> goes out, and
    /// the hub's answers come back and PARSE - a <c>Welcome</c>, the settings, and the scene of the
    /// screen the device reported.
    /// <para>
    /// Scanned rather than expected in order: after the welcome the hub sends the configuration and
    /// the scene, and which of those two arrives first is not part of the promise.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task TheRealClientGetsThroughTheRealHandshake()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var inbox = Channel.CreateUnbounded<ProtocolMessage>();
        var pump = Start(inbox, Channel.CreateUnbounded<ProtocolMessage>(), run.Token);

        var welcome = Assert.IsType<WelcomeMessage>(await Next(inbox, run.Token));
        Assert.Equal(Protocol.AssetPath, welcome.AssetPath);

        Assert.IsType<ConfigUpdateMessage>(await Until<ConfigUpdateMessage>(inbox, run.Token));

        var snapshot = await Until<SceneSnapshotMessage>(inbox, run.Token);
        Assert.Equal(Device, snapshot.Screen.Device);
        Assert.Equal(Screen, snapshot.Screen.Screen);

        await run.CancelAsync();
        await Finished(pump);
    }

    /// <summary>
    /// The sending half, which the handshake does NOT cover: the <c>Hello</c> goes out directly on
    /// the socket, while everything afterwards travels through the outbox and its own loop.
    /// <para>
    /// Shown through the heartbeat, because that is the one exchange the client answers by itself:
    /// the hub pings, the client pongs through the outbox, and the hub keeps the connection. With a
    /// deadline of two seconds and pings every two hundred milliseconds, surviving five seconds is
    /// only possible if the pongs are arriving - a broken send loop would have the hub hang up
    /// after the second one.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task TheClientsAnswersGetBackThroughTheOutbox()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var inbox = Channel.CreateUnbounded<ProtocolMessage>();
        var pump = Start(inbox, Channel.CreateUnbounded<ProtocolMessage>(), run.Token);

        await Until<WelcomeMessage>(inbox, run.Token);

        var pings = 0;
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow < until)
        {
            if (await Next(inbox, run.Token) is PingMessage)
            {
                pings++;
            }
        }

        // Well past the silence deadline, and still being pinged: the hub would have dropped a
        // display it had heard nothing from.
        Assert.True(pings > 5, $"only {pings} pings arrived - the connection did not survive");

        await run.CancelAsync();
        await Finished(pump);
    }

    private Task Start(
        Channel<ProtocolMessage> inbox, Channel<ProtocolMessage> outbox, CancellationToken cancellationToken)
    {
        var client = new DisplayClient(NullLogger<DisplayClient>.Instance);

        return Task.Run(
            () => client.RunAsync(_ws, Hello(), inbox.Writer, outbox, cancellationToken), cancellationToken);
    }

    private static HelloMessage Hello() =>
        new(
            Device,
            "SEAM",
            "1.0.0",
            Protocol.Version,
            [new ScreenInfo(Screen, "SEAM//DISPLAY1", null, new PixelSize(1920, 1080), 96, IsPrimary: true)],
            Token,
            null);

    private static async Task<T> Until<T>(Channel<ProtocolMessage> inbox, CancellationToken cancellationToken)
        where T : ProtocolMessage
    {
        while (true)
        {
            if (await Next(inbox, cancellationToken) is T wanted)
            {
                return wanted;
            }
        }
    }

    private static async Task<ProtocolMessage> Next(
        Channel<ProtocolMessage> inbox, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));

        return await inbox.Reader.ReadAsync(deadline.Token);
    }

    /// <summary>Cancelling is how the loop ends, so its cancellation is the expected outcome.</summary>
    private static async Task Finished(Task pump)
    {
        try
        {
            await pump;
        }
        catch (Exception exception) when (exception is OperationCanceledException or System.Net.WebSockets.WebSocketException)
        {
        }
    }

    private sealed class NoAssets : IAssetSource
    {
        public bool TryOpen(AssetId id, out Stream data, out string contentType) =>
            Nothing(out data, out contentType);

        public bool TryOpenThumb(AssetId id, int width, out Stream data, out string contentType) =>
            Nothing(out data, out contentType);

        private static bool Nothing(out Stream data, out string contentType)
        {
            data = Stream.Null;
            contentType = string.Empty;

            return false;
        }
    }
}
