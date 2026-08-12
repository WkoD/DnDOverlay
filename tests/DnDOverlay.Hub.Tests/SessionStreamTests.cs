using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The event stream without a socket: the fan-out on its own, and what
/// <see cref="ISessionApi.Subscribe"/> puts on it.
/// <para>
/// Over the wire next door is what needs a wire - a device that connected and knocked before
/// anybody subscribed, and a scene taken over out of a <c>Hello</c>.
/// </para>
/// </summary>
public sealed class SessionStreamTests
{
    private static readonly DeviceId Device = new(Guid.Parse("dddddddd-0000-0000-0000-000000000001"));
    private static readonly ScreenId Screen = new(@"\\?\DISPLAY#STREAM#1");

    /// <summary>
    /// The property the whole design rests on: the hub listens before any surface stands (rule 5),
    /// so a stream that began with changes alone would leave the surface waiting for events that
    /// are long past (Part 4).
    /// </summary>
    [Fact]
    public async Task Every_stream_begins_with_its_opening_picture()
    {
        var events = new SessionEvents();

        using var subscription = events.Open(() => new Beat(0));

        await using var stream = subscription
            .ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(new Beat(0), stream.Current);
    }

    /// <summary>
    /// Published before anybody was listening, and therefore gone. The opening picture is what says
    /// how things stand - an event from before it would be the same news twice, and for a patch it
    /// would be the same item twice (Part 4).
    /// </summary>
    [Fact]
    public async Task Nothing_from_before_the_subscription_turns_up_in_it()
    {
        var events = new SessionEvents();

        events.Publish(new Beat(-1));

        using var subscription = events.Open(() => new Beat(0));

        events.Publish(new Beat(1));

        Assert.Equal([new Beat(0), new Beat(1)], await TakeAsync(subscription, 2));
    }

    /// <summary>
    /// A stream of its own per call, and this is the reason it is not shared: with two control
    /// devices a shared one would have the second taking the first one's events away (Part 4).
    /// </summary>
    [Fact]
    public async Task A_second_subscriber_takes_nothing_from_the_first()
    {
        var events = new SessionEvents();

        using var first = events.Open(() => new Beat(0));

        events.Publish(new Beat(1));

        using var second = events.Open(() => new Beat(100));

        events.Publish(new Beat(2));

        Assert.Equal([new Beat(0), new Beat(1), new Beat(2)], await TakeAsync(first, 3));
        Assert.Equal([new Beat(100), new Beat(2)], await TakeAsync(second, 2));
    }

    /// <summary>
    /// A state event is never dropped, so a subscriber that cannot keep up is ENDED rather than
    /// served something stale - the same rule that governs a socket, and with the same way back:
    /// subscribing again yields a fresh opening picture (Part 4).
    /// </summary>
    [Fact]
    public async Task A_subscriber_that_falls_behind_is_cut_off_rather_than_served_stale()
    {
        var events = new SessionEvents();

        using var subscription = events.Open(() => new Beat(0));

        for (var i = 1; i <= SessionEvents.Capacity; i++)
        {
            events.Publish(new Beat(i));
        }

        var seen = 0;

        await foreach (var _ in subscription.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            seen++;
        }

        // The opening picture plus what fitted - and then the stream is over, rather than silently
        // skipping the rest.
        Assert.Equal(SessionEvents.Capacity, seen);
    }

    /// <summary>
    /// The counter-case, and the reason the class travels with the event rather than being decided
    /// per endpoint: what is transient may be dropped, and dropping it costs the subscriber nothing
    /// but the moment it described (Part 4).
    /// <para>
    /// Nothing in M1b publishes in this class - the traffic arrives with <c>TouchPoints</c> in M3.
    /// The test hands the class in expressly, exactly as the send-queue tests do.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_transient_event_is_dropped_rather_than_ending_the_stream()
    {
        var events = new SessionEvents();

        using var subscription = events.Open(() => new Beat(0));

        for (var i = 1; i <= SessionEvents.Capacity; i++)
        {
            events.Publish(new Flicker());
        }

        await using var stream = subscription
            .ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        for (var i = 0; i < SessionEvents.Capacity; i++)
        {
            Assert.True(await stream.MoveNextAsync());
        }

        // Still open, and still carrying state - which is the whole difference to the case above.
        events.Publish(new Beat(1));

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(new Beat(1), stream.Current);
    }

    /// <summary>Disposing unregisters, and nothing else does.</summary>
    [Fact]
    public async Task A_disposed_subscription_is_over()
    {
        var events = new SessionEvents();
        var subscription = events.Open(() => new Beat(0));

        subscription.Dispose();
        events.Publish(new Beat(1));

        Assert.Equal([new Beat(0)], await TakeAsync(subscription, 2));
    }

    /// <summary>
    /// The tree the device window is drawn from: every device the DM allowed, with its screens
    /// underneath - and a device that is switched off stays in it, because its wishes and
    /// parameters live here (Part 3, Part 7).
    /// </summary>
    [Fact]
    public async Task The_opening_picture_carries_a_known_device_that_is_not_connected()
    {
        using var session = Session(out var screens, out _, out _);

        // It was here once and went - the sequence that leaves a known device with known screens
        // and no socket.
        screens.Departed(Device, screens.Report(Device, [Info()], reported: null).Presence);

        var opening = await OpeningAsync(session);
        var device = Assert.Single(opening.Devices);

        Assert.Equal("TISCH-PC", device.Name);
        Assert.False(device.Connected);

        // The four that are only true while a socket is open say nothing rather than something
        // remembered from last week.
        Assert.Null(device.Address);
        Assert.Null(device.AppVersion);
        Assert.Null(device.RoundTrip);

        var screen = Assert.Single(device.Screens);

        Assert.Equal(ScreenState.Enabled, screen.State);
        Assert.Equal(SuppressReason.Unavailable, screen.Suppressed);
    }

    /// <summary>A wish set in the control comes back through the stream, like every other change.</summary>
    [Fact]
    public async Task A_screen_wish_arrives_as_an_event()
    {
        using var session = Session(out var screens, out _, out _);

        screens.Report(Device, [Info()], reported: null);

        await using var stream = await ListenAsync(session);

        await session.SetScreenStateAsync(new ScreenRef(Device, Screen), ScreenState.Blackout, TestContext.Current.CancellationToken);

        var change = await NextAsync<SessionEvent.DevicesChanged>(stream);

        Assert.Equal(ScreenState.Blackout, change.Devices.Single().Screens.Single().State);
    }

    /// <summary>
    /// A finding is published although it is never written - and that is the one place where "worth
    /// showing" and "worth keeping" part company. The catalogue's own change event means the second;
    /// the stream means the first (Part 3).
    /// </summary>
    [Fact]
    public async Task A_finding_arrives_although_it_is_never_persisted()
    {
        using var session = Session(out var screens, out _, out _);

        screens.Report(Device, [Info()], reported: null);

        var persisted = 0;

        screens.Changed += () => persisted++;

        await using var stream = await ListenAsync(session);

        await session.SuppressAsync(new ScreenRef(Device, Screen), SuppressReason.ControlWindow, TestContext.Current.CancellationToken);

        var change = await NextAsync<SessionEvent.DevicesChanged>(stream);

        Assert.Equal(SuppressReason.ControlWindow, change.Devices.Single().Screens.Single().Suppressed);
        Assert.Equal(0, persisted);
    }

    /// <summary>
    /// One command, one patch - and the same patch to both audiences, because a second control has
    /// to APPLY it. Handing it a whole scene instead would throw away what patches are for
    /// (Part 4, rule 1).
    /// </summary>
    [Fact]
    public async Task A_command_puts_its_patch_on_the_stream()
    {
        using var session = Session(out var screens, out _, out _);

        screens.Report(Device, [Info()], reported: null);

        await using var stream = await ListenAsync(session);

        var target = new ScreenRef(Device, Screen);
        var item = await session.AddItemAsync(target, Reference(), position: null, TestContext.Current.CancellationToken);

        var patched = await NextAsync<SessionEvent.ScenePatched>(stream);
        var op = Assert.Single(patched.Patch.Ops);

        Assert.Equal(target, op.Screen);
        Assert.Equal(item, Assert.IsType<AddItem>(op.Op).Item.ItemId);
    }

    /// <summary>
    /// Who is knocking and who was turned away, in one event - both move at the same moments,
    /// because a decision carries an entry from the one list to the other (Part 4).
    /// </summary>
    [Fact]
    public async Task The_pairing_desk_arrives_as_an_event()
    {
        using var session = Session(out _, out var pairing, out _);

        await using var stream = await ListenAsync(session);

        _ = pairing.Consider(Hello(), "10.0.0.7");

        var waiting = await NextAsync<SessionEvent.PairingChanged>(stream);

        Assert.Equal("4271", Assert.Single(waiting.Pending).PairingCode);
        Assert.Empty(waiting.Refused);

        await session.RejectAsync(Device, TestContext.Current.CancellationToken);

        var refused = await NextAsync<SessionEvent.PairingChanged>(stream, change => change.Refused.Count > 0);

        Assert.Empty(refused.Pending);
        Assert.Equal(RejectionReason.Denied, Assert.Single(refused.Refused).Reason);
    }

    /// <summary>
    /// Our own lines and forwarded ones go the same way, so what the DM reads on screen is what he
    /// finds again in the file (Part 8).
    /// </summary>
    [Fact]
    public async Task A_log_line_arrives_as_an_event()
    {
        using var session = Session(out _, out _, out var log);

        await using var stream = await ListenAsync(session);

        log.Add(Line());

        var logged = await NextAsync<SessionEvent.Logged>(stream);

        Assert.Equal(2001, logged.Record.EventId);
    }

    /// <summary>
    /// Built by hand rather than through the container, so the test holds every source it wants to
    /// move. The process log is in memory - a directory of null keeps everything in the ring.
    /// </summary>
    private static SessionApi Session(
        out ScreenCatalog screens,
        out PairingDirectory pairing,
        out ProcessLog log)
    {
        var options = new HubOptions
        {
            KnownDevices = [new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "a-token")],
        };

        screens = new ScreenCatalog();
        pairing = new PairingDirectory(Options.Create(options), TimeProvider.System);
        log = new ProcessLog(
            LogIdentity.Of(typeof(SessionStreamTests).Assembly, Protocol.Version),
            directory: null,
            LogFileLimits.Control,
            TimeProvider.System);

        return new SessionApi(
            new SceneStore(),
            screens,
            new DisplayConnections(),
            pairing,
            new SessionEvents(),
            log,
            NullLogger<SessionApi>.Instance);
    }

    private static async Task<SessionEvent.Opening> OpeningAsync(SessionApi session)
    {
        await using var stream = session
            .Subscribe(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        return await NextAsync<SessionEvent.Opening>(stream);
    }

    /// <summary>Subscribes and swallows the opening picture, so a test can watch for changes.</summary>
    private static async Task<IAsyncEnumerator<SessionEvent>> ListenAsync(SessionApi session)
    {
        var stream = session
            .Subscribe(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        _ = await NextAsync<SessionEvent.Opening>(stream);

        return stream;
    }

    /// <summary>
    /// Reads until the event this test is about arrives, passing over whatever else the hub has to
    /// say. Waiting for a POSITION would tie every test to how many things happen to be announced
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

    private static async Task<IReadOnlyList<SessionEvent>> TakeAsync(SessionEvents.Subscription subscription, int most)
    {
        var taken = new List<SessionEvent>();

        await foreach (var @event in subscription.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            taken.Add(@event);

            if (taken.Count == most)
            {
                break;
            }
        }

        return taken;
    }

    private static ScreenInfo Info() =>
        new(Screen, "TISCH-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, IsPrimary: true);

    private static HelloMessage Hello() =>
        new(Device, "TISCH-PC", "1.0.0", Protocol.Version, [Info()], null, "4271");

    private static AssetRef Reference() =>
        new(
            new AssetId(new string('d', 64)),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "Grimmbart");

    private static LogRecord Line() =>
        new(
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            LogLevel.Warning,
            2001,
            "AssetDownloadFailed",
            [],
            null,
            null,
            null);

    /// <summary>A state event with something to tell it apart by.</summary>
    private sealed record Beat(int Number) : SessionEvent;

    /// <summary>
    /// Transient, handed in by the test because nothing in M1b publishes in that class yet. The
    /// rule is built regardless, exactly as it is in front of a socket (Part 4, Part 10).
    /// </summary>
    private sealed record Flicker : SessionEvent
    {
        public override SendClass SendClass => SendClass.Transient;
    }
}
