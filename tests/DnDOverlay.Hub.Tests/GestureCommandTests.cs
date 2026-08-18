using DnDOverlay.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The commands M3a adds, seen from the side that decides: the hub hands out every revision, holds
/// what a sender may not decide, and answers a hand on a locked picture by doing nothing (Part 4).
/// </summary>
public sealed class GestureCommandTests
{
    private static readonly DeviceId Device = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000042"));
    private static readonly ScreenId Screen = new(@"\\?\DISPLAY#GESTURES#1");
    private static readonly ScreenRef Target = new(Device, Screen);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_transform_from_the_table_moves_the_authoritative_item()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        await session.TransformItemAsync(
            Target,
            new ItemTransform(item, 0.25, 0.75, 0.3, 90),
            fromTable: true,
            toFront: true,
            Cancellation);

        var moved = (await session.GetSceneAsync(Target, Cancellation)).Items.Single();

        Assert.Equal(0.25, moved.CenterX, precision: 9);
        Assert.Equal(0.75, moved.CenterY, precision: 9);
        Assert.Equal(90, moved.RotationDeg);
    }

    /// <summary>
    /// The hub holds what the sender may not decide. A display that asks for a place off the table
    /// does not get it - and it is not refused either, because a gesture that simply stopped
    /// working under the finger is worse than one that ends at the edge (Part 6).
    /// </summary>
    [Fact]
    public async Task A_place_off_the_table_is_held_at_the_edge_rather_than_refused()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        await session.TransformItemAsync(
            Target,
            new ItemTransform(item, 5, 0.5, 0.3, 0),
            fromTable: true,
            toFront: false,
            Cancellation);

        var moved = (await session.GetSceneAsync(Target, Cancellation)).Items.Single();

        Assert.True(moved.CenterX < 1.5, "the item was left where the display asked for it");
        Assert.Equal(moved, Manipulation.HoldAtEdge(moved, Context()));
    }

    [Fact]
    public async Task A_scale_beyond_the_bounds_is_held_between_them()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        await session.TransformItemAsync(
            Target,
            new ItemTransform(item, 0.5, 0.5, 500, 0),
            fromTable: true,
            toFront: false,
            Cancellation);

        Assert.Equal(
            Context().MaxScale,
            (await session.GetSceneAsync(Target, Cancellation)).Items.Single().Scale,
            precision: 9);
    }

    /// <summary>
    /// The lock guards against the table and not against the DM: the same command refused from a
    /// display goes through from the control, or every correction would begin with an unlock
    /// (Part 3).
    /// </summary>
    [Fact]
    public async Task A_locked_item_is_not_moved_from_the_table_and_is_moved_from_the_control()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.SetLockedAsync(Target, item, locked: true, Cancellation);

        var before = (await session.GetSceneAsync(Target, Cancellation)).Items.Single();

        await session.TransformItemAsync(
            Target,
            new ItemTransform(item, 0.1, 0.1, 0.2, 45),
            fromTable: true,
            toFront: true,
            Cancellation);

        Assert.Equal(before, (await session.GetSceneAsync(Target, Cancellation)).Items.Single());

        await session.TransformItemAsync(
            Target,
            new ItemTransform(item, 0.1, 0.1, 0.2, 45),
            fromTable: false,
            toFront: false,
            Cancellation);

        var moved = (await session.GetSceneAsync(Target, Cancellation)).Items.Single();

        Assert.Equal(0.1, moved.CenterX, precision: 9);
        Assert.True(moved.Locked, "the padlock was taken off by a movement");
    }

    /// <summary>What is taken hold of comes to the front, and only what is taken hold of (Part 3).</summary>
    [Fact]
    public async Task A_grab_brings_the_item_to_the_front_and_a_command_leaves_it_where_it_lies()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var lower = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        await session.TransformItemAsync(
            Target, new ItemTransform(lower, 0.4, 0.4, 0.3, 0), fromTable: true, toFront: true, Cancellation);

        var scene = await session.GetSceneAsync(Target, Cancellation);
        var raised = scene.Items.Single(item => item.ItemId == lower);

        Assert.Equal(scene.Items.Max(item => item.ZOrder), raised.ZOrder);

        // The rest of the gesture does not keep raising it: twenty reports a second would run the
        // number space up and change nothing anybody can see.
        await session.TransformItemAsync(
            Target, new ItemTransform(lower, 0.41, 0.4, 0.3, 0), fromTable: true, toFront: false, Cancellation);

        Assert.Equal(
            raised.ZOrder,
            (await session.GetSceneAsync(Target, Cancellation)).Items.Single(item => item.ItemId == lower).ZOrder);
    }

    /// <summary>Every transform gets its number from the hub, and they only ever go up (Part 4).</summary>
    [Fact]
    public async Task Revisions_are_handed_out_by_the_hub_and_rise()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        var revisions = new List<long>();

        for (var step = 0; step < 5; step++)
        {
            await session.TransformItemAsync(
                Target,
                new ItemTransform(item, 0.3 + (0.05 * step), 0.5, 0.3, 0),

                // A stale revision at the sender changes nothing here: a finger on the table is
                // the most recent truth there is, and the ordering is the hub's either way.
                fromTable: true,
                toFront: false,
                Cancellation);

            revisions.Add((await session.GetSceneAsync(Target, Cancellation)).Items.Single().Revision);
        }

        Assert.Equal([.. revisions.OrderBy(revision => revision)], revisions);
        Assert.Equal(revisions.Distinct().Count(), revisions.Count);
    }

    /// <summary>
    /// One command, one patch: unlocking all is a single patch of as many operations as there are
    /// locked items, so it is one step in the timeline and one undo (Part 4).
    /// </summary>
    [Fact]
    public async Task Unlocking_all_is_one_patch_and_touches_only_this_screen()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info(), Other()], reported: null);

        var here = new List<ItemId>();

        for (var index = 0; index < 3; index++)
        {
            var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
            await session.SetLockedAsync(Target, item, locked: true, Cancellation);
            here.Add(item);
        }

        var elsewhere = new ScreenRef(Device, Other().ScreenId);
        var other = await session.AddItemAsync(elsewhere, Reference(), position: null, Cancellation);
        await session.SetLockedAsync(elsewhere, other, locked: true, Cancellation);

        var stream = await ListenAsync(session);

        await session.UnlockAllAsync(Target, Cancellation);

        var patch = (await NextAsync<SessionEvent.ScenePatched>(stream)).Patch;

        Assert.Equal(3, patch.Ops.Count);
        Assert.All(patch.Ops, op => Assert.IsType<SetLocked>(op.Op));
        Assert.All((await session.GetSceneAsync(Target, Cancellation)).Items, item => Assert.False(item.Locked));
        Assert.True((await session.GetSceneAsync(elsewhere, Cancellation)).Items.Single().Locked);
    }

    /// <summary>A screen with nothing locked produces no patch at all - and therefore no undo step.</summary>
    [Fact]
    public async Task Unlocking_all_on_a_screen_with_nothing_locked_says_nothing()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        var stream = await ListenAsync(session);

        await session.UnlockAllAsync(Target, Cancellation);

        // Nothing is what has to be proved, so the next thing that happens has to be something
        // else: an operation that follows it is the only honest way to say "and no patch before
        // this one" without waiting on a clock.
        await session.SetLockedAsync(Target, (await session.GetSceneAsync(Target, Cancellation)).Items[0].ItemId, true, Cancellation);

        var patch = (await NextAsync<SessionEvent.ScenePatched>(stream)).Patch;

        Assert.Single(patch.Ops);
        Assert.True(((SetLocked)patch.Ops[0].Op).Locked, "the empty sweep sent a patch of its own");
    }

    /// <summary>
    /// Parking keeps size and rotation - the Java version reset both and undid the work of lining
    /// a picture up (Part 6) - and coming back out brings the picture to the front (Part 3).
    /// </summary>
    [Fact]
    public async Task Parking_keeps_size_and_rotation_and_unparking_comes_to_the_front()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        await session.TransformItemAsync(
            Target, new ItemTransform(item, 0.5, 0.5, 0.3, 90), fromTable: true, toFront: false, Cancellation);

        await session.ParkItemAsync(Target, item, parked: true, Cancellation);

        var parked = (await session.GetSceneAsync(Target, Cancellation)).Items.Single(one => one.ItemId == item);

        Assert.True(parked.Parked);
        Assert.Equal(0.3, parked.Scale, precision: 9);
        Assert.Equal(90, parked.RotationDeg);
        Assert.Equal(parked, Manipulation.HoldAtEdge(parked, Context()));

        await session.ParkItemAsync(Target, item, parked: false, Cancellation);

        var scene = await session.GetSceneAsync(Target, Cancellation);
        var back = scene.Items.Single(one => one.ItemId == item);

        Assert.False(back.Parked);
        Assert.Equal(scene.Items.Max(one => one.ZOrder), back.ZOrder);
    }

    [Fact]
    public async Task A_gesture_for_an_item_that_is_already_gone_is_not_an_error()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.RemoveItemAsync(Target, item, Cancellation);

        await session.TransformItemAsync(
            Target, new ItemTransform(item, 0.2, 0.2, 0.3, 0), fromTable: true, toFront: true, Cancellation);
        await session.ParkItemAsync(Target, item, parked: true, Cancellation);
        await session.SetLockedAsync(Target, item, locked: true, Cancellation);

        Assert.Empty((await session.GetSceneAsync(Target, Cancellation)).Items);
    }

    /// <summary>
    /// <b>A taken-over scene lifts the counter</b>, and the seam test is what found this: a control
    /// that restarts inherits items numbered by the run before it, and starting again at 1 would
    /// have every display weighing the hub's new state against a higher number of its own and
    /// keeping its own (Part 4, conflict resolution). Nothing would look broken - the table would
    /// simply stop following.
    /// </summary>
    [Fact]
    public async Task A_scene_taken_over_lifts_the_revision_counter_above_it()
    {
        using var session = Session(out var screens, out var scenes);
        screens.Report(Device, [Info()], reported: null);

        var item = new ItemId(Guid.NewGuid());

        scenes.Set(
            Target,
            SceneState.Empty with { Items = [Lying(item, revision: 4711)] });

        await session.TransformItemAsync(
            Target, new ItemTransform(item, 0.4, 0.4, 0.3, 0), fromTable: true, toFront: false, Cancellation);

        Assert.True(
            (await session.GetSceneAsync(Target, Cancellation)).Items.Single().Revision > 4711,
            "the hub handed out a number an item on the table already had");
    }

    /// <summary>
    /// Subscribes and swallows the opening picture, so what follows is what this test caused.
    /// Awaiting the subscription rather than starting it in the background is the difference
    /// between watching and racing.
    /// </summary>
    private static async Task<IAsyncEnumerator<SessionEvent>> ListenAsync(SessionApi session)
    {
        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        _ = await NextAsync<SessionEvent.Opening>(stream);

        return stream;
    }

    /// <summary>Reads until the event this test is about arrives, passing over the rest.</summary>
    private static async Task<T> NextAsync<T>(IAsyncEnumerator<SessionEvent> stream)
        where T : SessionEvent
    {
        while (await stream.MoveNextAsync())
        {
            if (stream.Current is T found)
            {
                return found;
            }
        }

        throw new InvalidOperationException($"the stream ended before a {typeof(T).Name} arrived");
    }

    private static SessionApi Session(out ScreenCatalog screens) => Session(out screens, out _);

    private static SessionApi Session(out ScreenCatalog screens, out SceneStore scenes)
    {
        var options = new HubOptions
        {
            KnownDevices = [new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "a-token")],
        };

        screens = new ScreenCatalog();
        scenes = new SceneStore();

        return new SessionApi(
            scenes,
            screens,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(options), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);
    }

    private static ScreenContext Context() => ScreenContext.Default(new PixelSize(1920, 1080), 96);

    private static ScreenInfo Info() =>
        new(Screen, "TISCH-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, true);

    private static ScreenInfo Other() =>
        new(new ScreenId(@"\\?\DISPLAY#GESTURES#2"), "TISCH-PC//DISPLAY2", null, new PixelSize(1920, 1080), 96, true);

    private static ImageItem Lying(ItemId item, long revision) =>
        new(
            item,
            CenterX: 0.5,
            CenterY: 0.5,
            Scale: 0.4,
            AspectRatio: 4d / 3d,
            RotationDeg: 0,
            ZOrder: 1,
            Locked: false,
            Parked: false,
            Revision: revision,
            AssetId: new AssetId(new string('d', 64)),
            Meta: new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            Name: "Grimmbart",
            ShowName: false,
            AnimationPaused: false);

    private static AssetRef Reference() =>
        new(
            new AssetId(new string('d', 64)),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "Grimmbart");
}
