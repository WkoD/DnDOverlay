using System.Net.WebSockets;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The event stream against a real socket - the part that cannot be shown without one.
/// <para>
/// What is proven here is the sequence nobody gets to see while developing: <b>the hub listens
/// before any surface stands</b> (rule 5). A display PC on autostart connects, hands over its
/// state and lodges a pairing request while the Surface is still building its stage, and the
/// opening picture is the only thing that keeps any of it from being lost (Part 4).
/// </para>
/// </summary>
public sealed class SessionStreamOverTheWireTests : IAsyncLifetime
{
    private const string Token = "a-token-that-was-issued-earlier";

    private static readonly DeviceId Device = new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"));
    private static readonly DeviceId Leaving = new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"));
    private static readonly DeviceId Carrying = new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000003"));
    private static readonly DeviceId Stranger = new(Guid.Parse("eeeeeeee-0000-0000-0000-000000000009"));

    private static readonly ScreenId First = new(@"\\?\DISPLAY#STREAMWIRE#1");

    private WebApplication _app = null!;
    private Uri _ws = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDnDOverlayHub(hub => hub.KnownDevices =
        [
            new PairedDevice(Device, "TISCH-PC", PairingRole.Display, Token),
            new PairedDevice(Leaving, "BEAMER-PC", PairingRole.Display, Token),
            new PairedDevice(Carrying, "FERNSEHER", PairingRole.Display, Token),
        ]);

        // There to be resolvable. Minimal APIs build ALL endpoints of a source on first access, so
        // an unresolvable IAssetSource makes the WEBSOCKET handshake answer 500 (checks/M1.md).
        builder.Services.AddSingleton<IAssetSource>(new NoAssets());

        _app = builder.Build();
        _app.UseWebSockets();
        _app.MapDnDOverlayHub();

        await _app.StartAsync();

        _ws = new Uri($"ws://{new Uri(_app.Urls.First()).Authority}{Protocol.DisplayPath}");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// The core case: a device connects and a second one knocks, and only THEN does a surface
    /// subscribe. Everything has to be in the first element - without it the surface would see
    /// nothing and would wait for events that are long past (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task The_opening_picture_carries_what_happened_before_anybody_subscribed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var connected = await ConnectAsync(Device, [Info(First)], cancellationToken);
        using var knocking = await ConnectAsync(Stranger, [Info(First)], cancellationToken, token: null);

        var connections = _app.Services.GetRequiredService<DisplayConnections>();
        var pairing = _app.Services.GetRequiredService<PairingDirectory>();

        await WaitUntilAsync(() => connections.Count == 1 && pairing.Pending.Count == 1, cancellationToken);

        // Only now is there a surface.
        await using var stream = Listen(cancellationToken);
        var opening = await NextAsync<SessionEvent.Opening>(stream);

        var device = Assert.Single(opening.Devices, view => view.Device == Device);

        Assert.True(device.Connected);
        Assert.Equal("127.0.0.1", device.Address);
        Assert.Equal("1.0.0", device.AppVersion);
        Assert.Equal(Protocol.Version, device.ProtocolVersion);
        Assert.Equal(First, Assert.Single(device.Screens).Screen.Screen);

        // And the one that is still waiting for the DM, with the code he compares against the
        // table.
        Assert.Equal("4271", Assert.Single(opening.Pending).PairingCode);
    }

    /// <summary>
    /// The device goes, its screens stay. Removing them would throw away the wish and the
    /// parameters, and preparing a scene for a switched-off table is exactly what has to keep
    /// working (Part 3).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_device_that_goes_stays_in_the_tree_with_its_screens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Listening BEFORE the device connects - see ListenAsync for why that order is the whole
        // difference between an event and a picture.
        await using var stream = await ListenAsync(cancellationToken);

        var socket = await ConnectAsync(Leaving, [Info(First)], cancellationToken);

        _ = await NextAsync<SessionEvent.DevicesChanged>(
            stream,
            change => change.Devices.Any(device => device.Device == Leaving && device.Connected));

        // The wish is set while it is here, so what comes back can be compared against something.
        var session = _app.Services.GetRequiredService<ISessionApi>();

        await session.SetScreenStateAsync(new ScreenRef(Leaving, First), ScreenState.Inactive, cancellationToken);

        socket.Abort();
        socket.Dispose();

        // Waited for the SETTLED picture, not the first one that mentions the departure. A leaving
        // device moves two sources - the connection list and the presence in the catalogue - so it
        // announces twice, and the events are whole lists precisely so that the last one is right
        // regardless of which arrived first.
        var gone = await NextAsync<SessionEvent.DevicesChanged>(
            stream,
            change => change.Devices.Any(device =>
                device.Device == Leaving
                && !device.Connected
                && device.Screens.All(screen => screen.Suppressed == SuppressReason.Unavailable)));

        var view = Assert.Single(gone.Devices, device => device.Device == Leaving);
        var screen = Assert.Single(view.Screens);

        Assert.Null(view.Address);
        Assert.Equal(ScreenState.Inactive, screen.State);
        Assert.Equal(SuppressReason.Unavailable, screen.Suppressed);
    }

    /// <summary>
    /// The one exception to "the hub is authoritative", seen from the surface: a control that has
    /// just restarted takes a table over from the display and gets it whole, because there is
    /// nothing to apply a patch to (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_taken_over_scene_arrives_whole()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var stream = await ListenAsync(cancellationToken);

        using var socket = await ConnectAsync(
            Carrying,
            [Info(First)],
            cancellationToken,
            scenes: [new ScreenScene(First, new SceneState(null, [Item()], true, true, []))]);

        var replaced = await NextAsync<SessionEvent.SceneReplaced>(stream);

        Assert.Equal(new ScreenRef(Carrying, First), replaced.Screen);
        Assert.Equal("Grimmbart", Assert.IsType<ImageItem>(Assert.Single(replaced.Scene.Items)).Name);
    }

    /// <summary>
    /// Subscribes and hands back the raw stream, opening picture and all. Only a test that is
    /// ABOUT the opening picture wants this one - everything else wants <see cref="ListenAsync"/>.
    /// </summary>
    private IAsyncEnumerator<SessionEvent> Listen(CancellationToken cancellationToken) =>
        _app.Services
            .GetRequiredService<ISessionApi>()
            .Subscribe(cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

    /// <summary>
    /// Subscribes and swallows the opening picture, so a test can watch for CHANGES - and it
    /// exists because doing that by hand is where the mistake gets made.
    /// <para>
    /// Whoever waits for an event has to be listening first. Subscribe after the thing has
    /// happened and its announcement is not an event at all: it sits in the opening picture, no
    /// change follows, and the wait runs to its timeout. Measured twice, both times on a loaded
    /// Linux runner while Windows had passed the same test for weeks. The in-memory sibling of
    /// this file has had this helper all along; this one had it missing, and exactly one test paid
    /// for it.
    /// </para>
    /// </summary>
    private async Task<IAsyncEnumerator<SessionEvent>> ListenAsync(CancellationToken cancellationToken)
    {
        var stream = Listen(cancellationToken);

        _ = await NextAsync<SessionEvent.Opening>(stream);

        return stream;
    }

    /// <summary>
    /// Reads until the event this test is about arrives, passing over whatever else the hub has to
    /// say first. Waiting for a POSITION would tie every test to how much happens to be announced
    /// alongside.
    /// </summary>
    private static async Task<T> NextAsync<T>(IAsyncEnumerator<SessionEvent> stream, Func<T, bool>? wanted = null)
        where T : SessionEvent
    {
        while (await stream.MoveNextAsync())
        {
            if (stream.Current is T found && (wanted is null || wanted(found)))
            {
                return found;
            }
        }

        throw new InvalidOperationException($"the stream ended before a {typeof(T).Name} arrived");
    }

    /// <summary>
    /// Waits for something the hub reaches on its own time - a message this test sent has been
    /// handed to a socket, not yet processed. Sleeping a fixed span would hold until the day a
    /// machine is slow.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> settled, CancellationToken cancellationToken)
    {
        while (!settled())
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    private async Task<ClientWebSocket> ConnectAsync(
        DeviceId device,
        IReadOnlyList<ScreenInfo> screens,
        CancellationToken cancellationToken,
        string? token = Token,
        IReadOnlyList<ScreenScene>? scenes = null)
    {
        var socket = new ClientWebSocket();

        await socket.ConnectAsync(_ws, cancellationToken);

        await socket.SendAsync(
            ProtocolJson.Serialise(new HelloMessage(
                device,
                "TISCH-PC",
                "1.0.0",
                Protocol.Version,
                screens,
                token,
                token is null ? "4271" : null,
                null,
                scenes)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

        return socket;
    }

    private static ScreenInfo Info(ScreenId screen) =>
        new(screen, $"TISCH-PC//{screen.Value[^1]}", null, new PixelSize(1920, 1080), 96, IsPrimary: true);

    private static ImageItem Item() =>
        new(
            new ItemId(Guid.NewGuid()), 0.5, 0.5, 0.5, 1.33, 0, 0, false, false, 1,
            new AssetId(new string('d', 64)),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "Grimmbart",
            false,
            false);

    /// <summary>There to be resolvable, not to serve anything.</summary>
#pragma warning disable CA1822 // Interface members; static would not satisfy IAssetSource.
    private sealed class NoAssets : IAssetSource
    {
        public bool TryOpen(AssetId id, out Stream data, out string contentType)
        {
            data = Stream.Null;
            contentType = "application/octet-stream";

            return false;
        }

        public bool TryOpenThumb(AssetId id, int width, out Stream data)
        {
            data = Stream.Null;

            return false;
        }
    }
#pragma warning restore CA1822
}
