using System.Net;
using System.Net.WebSockets;
using System.Text;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// M1a's whole point, checked end to end: a hardcoded asset travels from a command on the
/// control side to a display over a real WebSocket, and its bytes can be fetched.
/// <para>
/// Against a real Kestrel on a real socket rather than an in-memory pipeline. What has to be
/// proven here is that a command REACHES a display; a shortcut would prove the shortcut.
/// </para>
/// </summary>
public sealed class RunningThreadTests : IAsyncLifetime
{
    private static readonly AssetId Asset = new(new string('a', 64));

    /// <summary>
    /// Two devices that are already paired, and their token.
    /// <para>
    /// Since M1b a <c>Hello</c> without one does not get a <c>Welcome</c> - it waits for the DM,
    /// with no deadline anywhere (Part 4). These tests are about the running thread and not about
    /// pairing, so they take the shortest legitimate way in: the normal case at every power-on.
    /// </para>
    /// </summary>
    private const string Token = "a-token-that-was-issued-earlier";

    private static readonly DeviceId FirstDevice = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly DeviceId SecondDevice = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"));

    private WebApplication _app = null!;
    private Uri _http = null!;
    private Uri _ws = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDnDOverlayHub(hub => hub.KnownDevices =
        [
            new PairedDevice(FirstDevice, "TEST-PC", PairingRole.Display, Token),
            new PairedDevice(SecondDevice, "TEST-PC", PairingRole.Display, Token),
        ]);
        builder.Services.AddSingleton<IAssetSource>(new OneFakeAsset());

        _app = builder.Build();
        _app.UseWebSockets();
        _app.MapDnDOverlayHub();

        await _app.StartAsync();

        var address = new Uri(_app.Urls.First());
        _http = address;
        _ws = new Uri($"ws://{address.Authority}{Protocol.DisplayPath}");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>The reachability probe gives away nothing but "running" (Part 4).</summary>
    [Fact]
    public async Task Health_answers_without_a_token()
    {
        using var http = new HttpClient();

        var body = await http.GetStringAsync(new Uri(_http, Protocol.HealthPath), TestContext.Current.CancellationToken);

        Assert.Equal("running", body);
    }

    /// <summary>
    /// Without this check <c>GET /assets/..%5C..%5Cwindows%5C…</c> reads arbitrary files off the
    /// DM's machine, from any paired device (Part 4, Part 5).
    /// </summary>
    [Theory]
    [InlineData("..%5C..%5Cwindows%5Csystem.ini")]
    [InlineData("short")]
    [InlineData("NOTHEXNOTHEXNOTHEXNOTHEXNOTHEXNOTHEXNOTHEXNOTHEXNOTHEXNOTHEXNOTH")]
    public async Task An_identifier_that_is_not_a_hash_is_refused(string id)
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(
            new Uri(_http, $"{Protocol.AssetPath}/{id}"),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_bytes_of_a_known_asset_can_be_fetched()
    {
        using var http = new HttpClient();

        var bytes = await http.GetByteArrayAsync(
            new Uri(_http, $"{Protocol.AssetPath}/{Asset.Value}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(OneFakeAsset.Payload.Length, bytes.Length);
    }

    /// <summary>
    /// The thread itself: Hello in, Welcome and a snapshot per screen back, then a command from
    /// the control side arrives as a patch - and only at the device it concerns.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_command_reaches_the_display_that_it_concerns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var device = FirstDevice;
        var screen = new ScreenId(@"\\?\DISPLAY#TEST#1");
        var target = new ScreenRef(device, screen);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(_ws, cancellationToken);

        await SendAsync(socket, new HelloMessage(
            device,
            "TEST-PC",
            "1.0.0",
            Protocol.Version,
            [new ScreenInfo(screen, "TEST-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, true)],
            Token),
            cancellationToken);

        await ReceiveAsync<WelcomeMessage>(socket, cancellationToken);

        var snapshot = await ReceiveAsync<SceneSnapshotMessage>(socket, cancellationToken);
        Assert.Equal(target, snapshot.Screen);
        Assert.Empty(snapshot.Scene.Items);

        var session = _app.Services.GetRequiredService<ISessionApi>();
        var itemId = await session.AddItemAsync(target, Reference(), position: null, cancellationToken);

        var patch = await ReceiveAsync<ScenePatchMessage>(socket, cancellationToken);
        var op = Assert.Single(patch.Patch.Ops);

        Assert.Equal(target, op.Screen);

        var added = Assert.IsType<AddItem>(op.Op);
        var item = Assert.IsType<ImageItem>(added.Item);

        Assert.Equal(itemId, item.ItemId);
        Assert.Equal(Asset, item.AssetId);
        Assert.Equal("Grimmbart", item.Name);

        // The hub is authoritative, so what it kept and what it sent have to agree.
        var stored = await session.GetSceneAsync(target, cancellationToken);
        Assert.Equal(item, Assert.Single(stored.Items));
    }

    /// <summary>
    /// Five separately inserted images are five patches with five revisions - nothing is merged
    /// over a time window, however quickly they follow one another (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Five_commands_are_five_patches_with_five_revisions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var device = SecondDevice;
        var screen = new ScreenId(@"\\?\DISPLAY#TEST#2");
        var target = new ScreenRef(device, screen);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(_ws, cancellationToken);

        await SendAsync(socket, new HelloMessage(
            device,
            "TEST-PC",
            "1.0.0",
            Protocol.Version,
            [new ScreenInfo(screen, "TEST-PC//DISPLAY2", null, new PixelSize(1920, 1080), 96, true)],
            Token),
            cancellationToken);

        await ReceiveAsync<SceneSnapshotMessage>(socket, cancellationToken);

        var session = _app.Services.GetRequiredService<ISessionApi>();

        for (var i = 0; i < 5; i++)
        {
            await session.AddItemAsync(target, Reference(), position: null, cancellationToken);
        }

        var revisions = new List<long>();
        var zOrders = new List<int>();

        for (var i = 0; i < 5; i++)
        {
            var patch = await ReceiveAsync<ScenePatchMessage>(socket, cancellationToken);
            var item = Assert.IsType<ImageItem>(Assert.IsType<AddItem>(Assert.Single(patch.Patch.Ops).Op).Item);

            revisions.Add(item.Revision);
            zOrders.Add(item.ZOrder);
        }

        Assert.Equal(5, revisions.Distinct().Count());
        Assert.Equal([0, 1, 2, 3, 4], zOrders);
    }

    private static AssetRef Reference() =>
        new(Asset, new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)), "Grimmbart");

    private static async Task SendAsync(WebSocket socket, ProtocolMessage message, CancellationToken cancellationToken) =>
        await socket.SendAsync(
            ProtocolJson.Serialise(message),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    /// <summary>
    /// Answers the heartbeat and reads past it. A display does exactly this, and without it these
    /// tests would depend on staying shorter than one beat - which is the kind of assumption that
    /// holds until the day a machine is slow (Part 4).
    /// </summary>
    /// <summary>
    /// Reads until the message this test is about arrives, passing over whatever else the hub has
    /// to say first.
    /// <para>
    /// Counting messages instead would make every test here depend on how many things a connecting
    /// display is told - and that number grows with the milestones. The screen inventory added a
    /// <c>ConfigUpdate</c> in front of the snapshots, and three tests failed that had nothing to do
    /// with it. <b>A test should wait for what it asserts about, not for a position in a queue.</b>
    /// </para>
    /// </summary>
    private static async Task<T> ReceiveAsync<T>(WebSocket socket, CancellationToken cancellationToken)
        where T : ProtocolMessage
    {
        while (await ReceiveAsync(socket, cancellationToken) is { } message)
        {
            if (message is T wanted)
            {
                return wanted;
            }
        }

        throw new InvalidOperationException($"The connection ended before a {typeof(T).Name} arrived.");
    }

    private static async Task<ProtocolMessage?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            var message = ProtocolJson.Parse(buffer.AsSpan(0, result.Count));

            if (message is not PingMessage)
            {
                return message;
            }

            await SendAsync(socket, new PongMessage(), cancellationToken);
        }
    }

    private sealed class OneFakeAsset : IAssetSource
    {
        internal static byte[] Payload { get; } = Encoding.UTF8.GetBytes("not really a png, but bytes are bytes");

        public bool TryOpen(AssetId id, out Stream data, out string contentType)
        {
            if (id != Asset)
            {
                data = Stream.Null;
                contentType = string.Empty;
                return false;
            }

            data = new MemoryStream(Payload, writable: false);
            contentType = "image/png";

            return true;
        }
    }
}
