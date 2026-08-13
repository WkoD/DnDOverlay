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
/// The screen inventory over a real socket: what a connecting display is told, what it may say
/// back, and the one exception to "the hub is authoritative".
/// <para>
/// Against a real Kestrel rather than the catalogue alone. What has to be proven here is that
/// the rules survive the wire - the ordering, the takeover and the refusal are all things a
/// direct call would simply not exercise.
/// </para>
/// </summary>
public sealed class ScreenInventoryOverTheWireTests : IAsyncLifetime
{
    private const string Token = "a-token-that-was-issued-earlier";

    private static readonly DeviceId Device = new(Guid.Parse("cccccccc-0000-0000-0000-000000000001"));
    private static readonly DeviceId Restarted = new(Guid.Parse("cccccccc-0000-0000-0000-000000000002"));
    private static readonly DeviceId Plugged = new(Guid.Parse("cccccccc-0000-0000-0000-000000000003"));
    private static readonly DeviceId Talkative = new(Guid.Parse("cccccccc-0000-0000-0000-000000000004"));
    private static readonly DeviceId Returning = new(Guid.Parse("cccccccc-0000-0000-0000-000000000005"));
    private static readonly DeviceId Asked = new(Guid.Parse("cccccccc-0000-0000-0000-000000000006"));
    private static readonly DeviceId Bystander = new(Guid.Parse("cccccccc-0000-0000-0000-000000000007"));

    private static readonly ScreenId First = new(@"\\?\DISPLAY#WIRE#1");
    private static readonly ScreenId Second = new(@"\\?\DISPLAY#WIRE#2");

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
            new PairedDevice(Restarted, "TISCH-PC", PairingRole.Display, Token),
            new PairedDevice(Plugged, "TISCH-PC", PairingRole.Display, Token),
            new PairedDevice(Talkative, "TISCH-PC", PairingRole.Display, Token),
            new PairedDevice(Returning, "TISCH-PC", PairingRole.Display, Token),
            new PairedDevice(Asked, "TISCH-PC", PairingRole.Display, Token),
            new PairedDevice(Bystander, "BEAMER-PC", PairingRole.Display, Token),
        ]);

        // Registered although no test here fetches an asset - and that is not belt and braces.
        // Minimal APIs build ALL endpoints of a source on first access, so an unresolvable
        // parameter on /assets/{id} makes the WEBSOCKET handshake answer 500, and the message
        // points at the call rather than at the cause. A service nobody registered is not a
        // problem of the endpoint that needs it, but of all of them (checks/M1.md).
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
    /// A display starts silent - every screen inactive, no window anywhere. What puts one down is
    /// the control saying so, and it has to arrive BEFORE the scenes or the device would hold an
    /// arrangement it has nowhere to draw (Part 3).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_connecting_display_is_told_how_its_screens_stand()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var socket = await ConnectAsync(Device, [Info(First), Info(Second)], cancellationToken);

        await ReceiveAsync<WelcomeMessage>(socket, cancellationToken);

        var update = await ReceiveAsync<ConfigUpdateMessage>(socket, cancellationToken);

        Assert.Equal(2, update.Update.Screens.Count);
        Assert.All(update.Update.Screens, screen => Assert.Equal(ScreenState.Enabled, screen.Command!.State));
        Assert.All(update.Update.Screens, screen => Assert.Null(screen.Command!.Suppress));
    }

    /// <summary>
    /// The wish reaches the device, and it is the one thing that travels in one direction only.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_state_set_in_the_control_reaches_the_device()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var socket = await ConnectAsync(Device, [Info(First)], cancellationToken);

        await ReceiveAsync<ConfigUpdateMessage>(socket, cancellationToken);

        var session = _app.Services.GetRequiredService<ISessionApi>();
        var target = new ScreenRef(Device, First);

        await session.SetScreenStateAsync(target, ScreenState.Blackout, cancellationToken);

        var update = await ReceiveAsync<ConfigUpdateMessage>(socket, cancellationToken);
        var screen = Assert.Single(update.Update.Screens);

        Assert.Equal(ScreenState.Blackout, screen.Command!.State);

        // And a finding travels next to it without touching the wish.
        await session.SuppressAsync(target, SuppressReason.ControlWindow, cancellationToken);

        var suppressed = Assert.Single((await ReceiveAsync<ConfigUpdateMessage>(socket, cancellationToken)).Update.Screens);

        Assert.Equal(SuppressReason.ControlWindow, suppressed.Command!.Suppress);
        Assert.Equal(ScreenState.Blackout, suppressed.Command.State);
        Assert.Equal(ScreenState.Blackout, session.Screens.Single(view => view.Screen == target).State);
    }

    /// <summary>
    /// "Which one are you?" goes to the device that was asked and to no other. With two devices of
    /// two screens each this is the only thing that says which tile is which physical screen, and
    /// a second table lighting up with names would be exactly the confusion it exists to end
    /// (Part 6).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Identifying_screens_reaches_that_device_and_no_other()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var asked = await ConnectAsync(Asked, [Info(First), Info(Second)], cancellationToken);
        using var bystander = await ConnectAsync(Bystander, [Info(First)], cancellationToken);

        await ReceiveAsync<ConfigUpdateMessage>(asked, cancellationToken);
        await ReceiveAsync<ConfigUpdateMessage>(bystander, cancellationToken);

        var session = _app.Services.GetRequiredService<ISessionApi>();

        await session.IdentifyScreensAsync(Asked, cancellationToken);

        // It carries nothing: arriving IS the whole message, and the device names its own screens.
        await ReceiveAsync<IdentifyScreensMessage>(asked, cancellationToken);

        // What the bystander must NOT have got is proven without waiting for a silence: give it
        // something it definitely will get, and assert that this is the next thing to arrive.
        await session.SetScreenStateAsync(new ScreenRef(Bystander, First), ScreenState.Blackout, cancellationToken);

        var next = await ReceiveAsync<ConfigUpdateMessage>(bystander, cancellationToken);

        Assert.Equal(ScreenState.Blackout, Assert.Single(next.Update.Screens).Command!.State);
    }

    /// <summary>
    /// A device that is switched off is simply not asked. Unlike a setting - which is kept and goes
    /// out with the next connection - an identification is only ever worth anything now.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Identifying_a_device_that_is_not_connected_does_nothing()
    {
        var session = _app.Services.GetRequiredService<ISessionApi>();

        await session.IdentifyScreensAsync(Asked, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The one exception to "the hub is authoritative", and deliberately this narrow: a control
    /// that has just restarted takes the arrangement over from the display - but only where it
    /// has none of its own (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_scene_the_hub_does_not_have_is_taken_over_from_the_device()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = new ScreenRef(Restarted, First);
        var item = Item();

        using var socket = await ConnectAsync(
            Restarted,
            [Info(First)],
            cancellationToken,
            scenes: [new ScreenScene(First, new SceneState(null, [item], true, true, []))]);

        await ReceiveAsync<WelcomeMessage>(socket, cancellationToken);

        var session = _app.Services.GetRequiredService<ISessionApi>();
        var scene = await session.GetSceneAsync(target, cancellationToken);

        Assert.Equal(item, Assert.Single(scene.Items));

        // And the snapshot the hub sends back is that same scene - taken over, then put through.
        var snapshot = await ReceiveAsync<SceneSnapshotMessage>(socket, cancellationToken);

        Assert.Equal(target, snapshot.Screen);
        Assert.Equal(item, Assert.Single(snapshot.Scene.Items));
    }

    /// <summary>
    /// Where the hub has a scene of its own it puts THAT through instead - the takeover is bounded
    /// to the start, not a way for a device to write into the hub (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_scene_the_hub_has_is_put_through_instead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = new ScreenRef(Restarted, Second);

        _app.Services.GetRequiredService<SceneStore>().Set(target, SceneState.Empty);

        using var socket = await ConnectAsync(
            Restarted,
            [Info(Second)],
            cancellationToken,
            scenes: [new ScreenScene(Second, new SceneState(null, [Item()], true, true, []))]);

        await ReceiveAsync<WelcomeMessage>(socket, cancellationToken);

        var snapshot = await ReceiveAsync<SceneSnapshotMessage>(socket, cancellationToken);

        Assert.Equal(target, snapshot.Screen);
        Assert.Empty(snapshot.Scene.Items);
    }

    /// <summary>
    /// A display that crashed and came straight back. The old handler tidies up AFTER the new one
    /// is already serving, and its clean-up works by DEVICE - so without a guard it would mark the
    /// live connection's screens unavailable, and the tile would say "not played on" about a table
    /// that is right there (Part 3).
    /// <para>
    /// Made deterministic by draining the old socket until the server closes it: that close comes
    /// strictly after the clean-up, so there is nothing to sleep for.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_fast_reconnect_does_not_have_the_old_handler_disable_the_new_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = new ScreenRef(Returning, First);

        using var stale = await ConnectAsync(Returning, [Info(First)], cancellationToken);

        await ReceiveAsync<WelcomeMessage>(stale, cancellationToken);

        // From here on nobody answers this socket's pings, so the probe finds silence and this
        // connection is replaced rather than taken for a clone (Part 4).
        using var fresh = await ConnectAsync(Returning, [Info(First)], cancellationToken);

        await ReceiveAsync<WelcomeMessage>(fresh, cancellationToken);
        await DrainUntilClosedAsync(stale, cancellationToken);

        var catalog = _app.Services.GetRequiredService<ScreenCatalog>();

        Assert.Null(catalog.ViewOf(target)!.Suppressed);
        Assert.Equal(1, _app.Services.GetRequiredService<DisplayConnections>().Count);
    }

    /// <summary>
    /// A hot-plug on a standing connection. Without following it a patch for the new screen would
    /// be addressed at nobody - the connection carries the screens it is responsible for
    /// (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_screen_plugged_in_mid_connection_can_be_played_on()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var socket = await ConnectAsync(Plugged, [Info(First)], cancellationToken);

        await ReceiveAsync<SceneSnapshotMessage>(socket, cancellationToken);

        await SendAsync(socket, new ScreensChangedMessage([Info(First), Info(Second)]), cancellationToken);

        // The hub sends a snapshot for the newcomer, so it does not sit empty until the DM next
        // touches it.
        var target = new ScreenRef(Plugged, Second);
        var snapshot = await ReceiveAsync<SceneSnapshotMessage>(socket, message => message.Screen == target, cancellationToken);

        Assert.Empty(snapshot.Scene.Items);

        var session = _app.Services.GetRequiredService<ISessionApi>();
        await session.AddItemAsync(target, Reference(), position: null, cancellationToken);

        var patch = await ReceiveAsync<ScenePatchMessage>(socket, cancellationToken);

        Assert.Equal(target, Assert.Single(patch.Patch.Ops).Screen);
    }

    /// <summary>
    /// A device may say how it is SET and never how it STANDS. The settings half of the very same
    /// message is applied - only the state is refused (Part 3, Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task A_device_may_change_settings_but_not_states()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = new ScreenRef(Talkative, First);

        using var socket = await ConnectAsync(Talkative, [Info(First)], cancellationToken);

        await ReceiveAsync<SceneSnapshotMessage>(socket, cancellationToken);

        var session = _app.Services.GetRequiredService<ISessionApi>();
        await session.SetScreenStateAsync(target, ScreenState.Inactive, cancellationToken);
        await ReceiveAsync<ConfigUpdateMessage>(socket, cancellationToken);

        await SendAsync(
            socket,
            new ConfigUpdateMessage(new ConfigUpdate(
            [
                new ScreenConfigUpdate(
                    First,
                    new ScreenSettings(ParkEdge: ParkEdge.Bottom),
                    new ScreenCommand(ScreenState.Enabled)),
            ])),
            cancellationToken);

        var catalog = _app.Services.GetRequiredService<ScreenCatalog>();

        await WaitUntilAsync(
            () => catalog.ContextFor(target).ParkEdge == ParkEdge.Bottom,
            cancellationToken);

        Assert.Equal(ScreenState.Inactive, catalog.ViewOf(target)!.State);
    }

    /// <summary>
    /// The baseline of the two-sided configuration: what the device reports in its <c>Hello</c>
    /// becomes the control's - without which the same value would have two writers and no
    /// reconciling (Part 4).
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task The_hello_carries_the_baseline_and_the_control_takes_it_over()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = new ScreenRef(Talkative, Second);

        using var socket = await ConnectAsync(
            Talkative,
            [Info(Second)],
            cancellationToken,
            settings: new ConfigUpdate(
                [new ScreenConfigUpdate(Second, new ScreenSettings(Placement: PlacementMode.Cascade, MaxScale: 3))]));

        await ReceiveAsync<WelcomeMessage>(socket, cancellationToken);

        var context = _app.Services.GetRequiredService<ScreenCatalog>().ContextFor(target);

        Assert.Equal(PlacementMode.Cascade, context.Placement);
        Assert.Equal(3, context.MaxScale);
    }

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

    private static AssetRef Reference() =>
        new(new AssetId(new string('d', 64)), new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)), "Grimmbart");

    private async Task<ClientWebSocket> ConnectAsync(
        DeviceId device,
        IReadOnlyList<ScreenInfo> screens,
        CancellationToken cancellationToken,
        ConfigUpdate? settings = null,
        IReadOnlyList<ScreenScene>? scenes = null)
    {
        var socket = new ClientWebSocket();

        await socket.ConnectAsync(_ws, cancellationToken);

        await SendAsync(
            socket,
            new HelloMessage(device, "TISCH-PC", "1.0.0", Protocol.Version, screens, Token, null, settings, scenes),
            cancellationToken);

        return socket;
    }

    /// <summary>
    /// Waits for a condition the hub reaches on its own time - a message this test sent has been
    /// handed to a socket, not yet processed. Polling a state is honest here; sleeping a fixed
    /// span would hold until the day a machine is slow.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> settled, CancellationToken cancellationToken)
    {
        while (!settled())
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    /// <summary>
    /// Reads a socket until the server has finished with it. Deliberately answering nothing: this
    /// is the connection that is meant to look silent.
    /// </summary>
    private static async Task DrainUntilClosedAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
            }
        }
        catch (WebSocketException)
        {
            // Dropped rather than closed politely - which is just as good an answer to "is the
            // server done with this one?".
        }
    }

    private static async Task SendAsync(WebSocket socket, ProtocolMessage message, CancellationToken cancellationToken) =>
        await socket.SendAsync(
            ProtocolJson.Serialise(message),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    private static Task<T> ReceiveAsync<T>(WebSocket socket, CancellationToken cancellationToken)
        where T : ProtocolMessage =>
        ReceiveAsync<T>(socket, _ => true, cancellationToken);

    /// <summary>
    /// Reads until the message this test is about arrives, passing over whatever else the hub has
    /// to say first - and answering the heartbeat, as a display does. Waiting for a POSITION in
    /// the queue would tie every test to how many things a connecting display is told.
    /// </summary>
    private static async Task<T> ReceiveAsync<T>(
        WebSocket socket,
        Func<T, bool> wanted,
        CancellationToken cancellationToken)
        where T : ProtocolMessage
    {
        var buffer = new byte[64 * 1024];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            var message = ProtocolJson.Parse(buffer.AsSpan(0, result.Count));

            if (message is PingMessage)
            {
                await SendAsync(socket, new PongMessage(), cancellationToken);
                continue;
            }

            if (message is T found && wanted(found))
            {
                return found;
            }
        }
    }
}
