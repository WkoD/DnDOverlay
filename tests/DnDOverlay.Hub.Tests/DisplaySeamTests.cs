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
    /// Scanned rather than expected in order, and that holds for the WELCOME too. The heartbeat
    /// starts at the socket rather than at the admission - deliberately, so that a connection still
    /// waiting for the DM is covered by it (Part 4) - so a Ping may legitimately arrive before the
    /// Welcome. This test used to take the first message and name it, which held on a fast machine
    /// and fell over on a loaded CI runner the first time this branch reached Linux: it asserted an
    /// order nobody had promised.
    /// </para>
    /// <para>
    /// Then the configuration and the scene, and which of those two comes first is not part of the
    /// promise either.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task TheRealClientGetsThroughTheRealHandshake()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var inbox = Channel.CreateUnbounded<ProtocolMessage>();
        var pump = Start(inbox, new TaskCompletionSource<SendQueues>(), run.Token);

        var welcome = await Until<WelcomeMessage>(inbox, run.Token);
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
        var pump = Start(inbox, new TaskCompletionSource<SendQueues>(), run.Token);

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

    /// <summary>
    /// The gesture wire, end to end: a real client sends what a hand at the table produces, and the
    /// hub answers with the patch that everybody else on that table will apply.
    /// <para>
    /// It is the third crossing of this seam and the first that carries an intention UPWARDS. Both
    /// halves have their own tests - the client's send loop here, the hub's command surface in
    /// <c>GestureCommandTests</c> against a scene built by hand - and that is exactly the
    /// arrangement that let an <c>AssetClient</c> pass without ever sending a token.
    /// </para>
    /// <para>
    /// The item comes in with the <c>Hello</c>, over the takeover path, because a display cannot
    /// put an image on a table by itself: what it can do is report what a player did to one that is
    /// already lying there.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task AGestureFromTheClientMovesTheItemAndComesBackAsAPatch()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var item = new ItemId(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var inbox = Channel.CreateUnbounded<ProtocolMessage>();
        var outbox = new TaskCompletionSource<SendQueues>();

        var pump = Start(inbox, outbox, run.Token, Lying(item));

        await Until<WelcomeMessage>(inbox, run.Token);

        // The takeover is what puts the item into the hub's scene, and it has to have happened
        // before the gesture means anything - a transform for an item nobody has is a no-op, and
        // this test would then be proving that nothing happens.
        var snapshot = await Until<SceneSnapshotMessage>(inbox, run.Token);

        Assert.Equal(item, Assert.Single(snapshot.Scene.Items).ItemId);

        Assert.True((await outbox.Task).TrySend(
            new ItemTransformedMessage(
                Screen,
                new ItemTransform(item, 0.8, 0.3, 0.25, 90),
                KnownRevision: 1,
                Grabbed: true)));

        var patch = await Until<ScenePatchMessage>(inbox, run.Token);
        var op = Assert.IsType<TransformItem>(Assert.Single(patch.Patch.Ops).Op);

        Assert.Equal(item, op.Item);
        Assert.Equal(0.8, op.CenterX, precision: 6);
        Assert.Equal(90, op.RotationDeg);

        // The hub's number, not the one the client sent up: an intention goes up, a fact comes
        // back (Part 4).
        Assert.True(op.Revision > 1, "the revision came back unchanged - the hub did not hand one out");

        await run.CancelAsync();
        await Finished(pump);
    }

    /// <summary>
    /// The other half of the same wire: a swipe into the slot bar. It is the gesture the players use
    /// most to clear the table, and until M3b there was no message for it at all - Part 4 has the
    /// operation and never gave the table a way to ask for it.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task AParkFromTheClientPutsTheItemIntoTheBar()
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var item = new ItemId(Guid.Parse("11111111-2222-3333-4444-666666666666"));
        var inbox = Channel.CreateUnbounded<ProtocolMessage>();
        var outbox = new TaskCompletionSource<SendQueues>();

        var pump = Start(inbox, outbox, run.Token, Lying(item));

        await Until<WelcomeMessage>(inbox, run.Token);
        await Until<SceneSnapshotMessage>(inbox, run.Token);

        Assert.True((await outbox.Task).TrySend(new ItemParkedMessage(Screen, item, Parked: true)));

        var patch = await Until<ScenePatchMessage>(inbox, run.Token);
        var op = Assert.IsType<ParkItem>(Assert.Single(patch.Patch.Ops).Op);

        Assert.Equal(item, op.Item);
        Assert.True(op.Parked);

        // Where it now lies is NOT in the message and not in the operation - both ends work it out
        // from the list of parked pictures and the screen's park edge.
        await run.CancelAsync();
        await Finished(pump);
    }

    /// <summary>
    /// Starts a real client and hands back the queues it sends through, so a test can say what a
    /// hand at the table would have said.
    /// <para>
    /// Since M3c the outgoing side is <see cref="SendQueues"/> at both ends of the wire rather
    /// than a channel at this one, so the way to speak is to be handed the queues when the socket
    /// stands (Part 4, M3 siting question 2).
    /// </para>
    /// </summary>
    private Task Start(
        Channel<ProtocolMessage> inbox,
        TaskCompletionSource<SendQueues> outbox,
        CancellationToken cancellationToken,
        IReadOnlyList<ScreenScene>? scenes = null)
    {
        var client = new DisplayClient(NullLogger<DisplayClient>.Instance);

        return Task.Run(
            () => client.RunAsync(
                _ws,
                Hello(scenes),
                inbox.Writer,
                Limits,
                queues => outbox.SetResult(queues),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>The real ceilings; nothing in these tests goes anywhere near them.</summary>
    private static SendLimits Limits { get; } =
        new(MaxStateMessages: 256, MaxStateBytes: 8 * 1024 * 1024, MaxTransientSlots: 8, TimeSpan.FromSeconds(10));

    private static HelloMessage Hello(IReadOnlyList<ScreenScene>? scenes = null) =>
        new(
            Device,
            "SEAM",
            "1.0.0",
            Protocol.Version,
            [new ScreenInfo(Screen, "SEAM//DISPLAY1", null, new PixelSize(1920, 1080), 96, IsPrimary: true)],
            Token,
            null,
            null,
            scenes);

    /// <summary>One picture already on the table, as a device that survived a control restart has.</summary>
    private static IReadOnlyList<ScreenScene> Lying(ItemId item) =>
    [
        new(
            Screen,
            SceneState.Empty with
            {
                Items =
                [
                    new ImageItem(
                        item,
                        CenterX: 0.5,
                        CenterY: 0.5,
                        Scale: 0.4,
                        AspectRatio: 4d / 3d,
                        RotationDeg: 0,
                        ZOrder: 1,
                        Locked: false,
                        Parked: false,
                        Revision: 1,
                        AssetId: new AssetId(new string('e', 64)),
                        Meta: new AssetMeta(800, 600, "png", 1024, false, new string('f', 64)),
                        Name: "Grimmbart",
                        ShowName: false,
                        AnimationPaused: false),
                ],
            }),
    ];

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
