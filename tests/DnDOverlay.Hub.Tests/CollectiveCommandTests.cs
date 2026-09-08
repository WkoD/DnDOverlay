using System.Globalization;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// One command of the DM, one patch - however many pictures it touches.
/// <para>
/// <c>ScenePatch</c> has said since M1 that a command produces "exactly one patch, with as many
/// operations as that one command needs". The control did not: an intake sent one <c>AddItem</c>
/// per picture, and the thumbnail's menu one command per selected item, unawaited. Measured at the
/// table on 07.09.2026, a run of 714 filled a subscriber queue that holds 256, the hub ended that
/// stream as it must, and the control went deaf for the rest of the evening.
/// </para>
/// </summary>
public sealed class CollectiveCommandTests
{
    private static readonly DeviceId Device = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"));
    private static readonly ScreenId Screen = new(@"\\?\DISPLAY#COLLECTIVE#1");
    private static readonly ScreenId Second = new(@"\\?\DISPLAY#COLLECTIVE#2");
    private static readonly ScreenRef Target = new(Device, Screen);
    private static readonly ScreenRef Other = new(Device, Second);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// The whole point, and the reason the method exists: what used to cut the stream now goes
    /// through it in one piece.
    /// </summary>
    [Fact]
    public async Task A_run_larger_than_a_subscriber_queue_arrives_as_one_patch()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        await using (stream.ConfigureAwait(false))
        {
            Assert.True(await stream.MoveNextAsync());
            _ = Assert.IsType<SessionEvent.Opening>(stream.Current);

            const int Run = SessionEvents.Capacity + 44;

            var added = await session.AddItemsAsync(Target, [.. Many(Run)], Cancellation);

            Assert.Equal(Run, added.Count);

            // ONE event for the whole run, carrying one operation per picture - where the old shape
            // put 300 events into a queue of 256 and had the stream ended under it.
            Assert.True(await stream.MoveNextAsync());

            var patched = Assert.IsType<SessionEvent.ScenePatched>(stream.Current);

            Assert.Equal(Run, patched.Patch.Ops.Count);
            Assert.All(patched.Patch.Ops, op => Assert.IsType<AddItem>(op.Op));
        }
    }

    /// <summary>
    /// <b>The trap the fold exists for.</b> Placement counts what already lies on the screen and the
    /// depth comes from the top of the scene, so a run worked out against the SAME starting scene
    /// would put every picture on one grid place at one depth - seven hundred pictures exactly on
    /// top of each other, looking for all the world like one.
    /// </summary>
    [Fact]
    public async Task Every_picture_of_a_run_gets_its_own_depth_and_not_all_one_place()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        _ = await session.AddItemsAsync(Target, [.. Many(12)], Cancellation);

        var items = (await session.GetSceneAsync(Target, Cancellation)).Items.OfType<ImageItem>().ToList();

        Assert.Equal(12, items.Count);

        // One depth per picture, strictly rising: what is chosen first lies underneath (Part 7).
        Assert.Equal(12, items.Select(item => item.ZOrder).Distinct().Count());
        Assert.Equal([.. items.Select(item => item.ZOrder).Order()], [.. items.Select(item => item.ZOrder)]);

        // The grid wraps, so places repeat - but they must not all be one.
        Assert.True(
            items.Select(item => (item.CenterX, item.CenterY)).Distinct().Count() > 1,
            "every picture of the run landed on one place - the scene was not folded");
    }

    /// <summary>
    /// The same fold seen from the other side, and the assertion is deliberately on the way OUT of
    /// the fan.
    /// <para>
    /// Going in, each card takes its order from <c>NextRevision</c>, which is monotonic whether or
    /// not the scene is folded - so distinct <c>ParkedAt</c> values prove nothing about folding.
    /// Coming out, the depth is <c>TopZOrder + 1</c> and is therefore read from the scene: without
    /// the fold every card of the selection would be handed the SAME depth and the order the DM had
    /// in the fan would be flattened on the way back to the table.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Taking_a_whole_selection_back_out_of_the_fan_keeps_its_order()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(5)], Cancellation);

        await session.ParkItemsAsync(Target, added, parked: true, Cancellation);

        var parked = (await session.GetSceneAsync(Target, Cancellation))
            .Items.Where(item => item.Parked).ToList();

        Assert.Equal(5, parked.Count);
        Assert.Equal(5, parked.Select(item => item.ParkedAt).Distinct().Count());

        await session.ParkItemsAsync(Target, added, parked: false, Cancellation);

        var back = (await session.GetSceneAsync(Target, Cancellation)).Items;

        Assert.All(back, item => Assert.False(item.Parked));

        // Five depths, not one: the scene was folded between one card and the next.
        Assert.Equal(5, back.Select(item => item.ZOrder).Distinct().Count());
    }

    /// <summary>Removing a selection is one patch, not one per picture.</summary>
    [Fact]
    public async Task Removing_a_selection_is_one_patch()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(6)], Cancellation);

        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        await using (stream.ConfigureAwait(false))
        {
            Assert.True(await stream.MoveNextAsync());

            await session.RemoveItemsAsync(Target, [.. added.Take(4)], Cancellation);

            Assert.True(await stream.MoveNextAsync());

            var patched = Assert.IsType<SessionEvent.ScenePatched>(stream.Current);

            Assert.Equal(4, patched.Patch.Ops.Count);
            Assert.All(patched.Patch.Ops, op => Assert.IsType<RemoveItem>(op.Op));
        }

        Assert.Equal(2, (await session.GetSceneAsync(Target, Cancellation)).Items.Count);
    }

    /// <summary>
    /// An item that has gone by the time the command runs is skipped rather than refused - with two
    /// ways of steering and hands on the table that is a normal course of events (Part 11). What is
    /// left still travels as one patch.
    /// </summary>
    [Fact]
    public async Task A_selection_holding_an_item_that_has_gone_still_acts_on_the_rest()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(3)], Cancellation);

        await session.RemoveItemAsync(Target, added[1], Cancellation);
        await session.SetItemsLockedAsync(Target, added, locked: true, Cancellation);

        var items = (await session.GetSceneAsync(Target, Cancellation)).Items;

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.True(item.Locked));
    }

    /// <summary>A selection that comes down to nothing produces no patch at all, not an empty one.</summary>
    [Fact]
    public async Task A_selection_of_nothing_says_nothing()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        await using (stream.ConfigureAwait(false))
        {
            Assert.True(await stream.MoveNextAsync());

            await session.RemoveItemsAsync(Target, [], Cancellation);
            _ = await session.AddItemsAsync(Target, [], Cancellation);

            // Something else afterwards, so the test cannot pass by reading nothing at all.
            await session.ToggleItemsAsync(Target, visible: false, Cancellation);

            Assert.True(await stream.MoveNextAsync());

            var patched = Assert.IsType<SessionEvent.ScenePatched>(stream.Current);

            _ = Assert.IsType<ToggleItems>(Assert.Single(patched.Patch.Ops).Op);
        }
    }

    /// <summary>
    /// <b>The test this whole thread is about.</b> A selection carries the order it was PICKED in,
    /// which is not the order the DM sees on the table - so the selection is handed over here in
    /// exactly the wrong order, and the stack still has to come out right on the other side.
    /// <para>
    /// It fails in two different ways without the sort, and both matter. Landing hands out rising
    /// depths as the run proceeds, so a scrambled run rebuilds a scrambled stack; and before the
    /// collective form existed at all, the menu fired one unawaited command per picture, which put
    /// the order at the mercy of whichever call reached the gate first.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_moved_selection_lies_on_the_target_in_the_order_it_had_on_the_source()
    {
        using var session = Session(out var screens);
        screens.Report(Device, Two(), reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(5)], Cancellation);
        var stacked = await ByDepth(session, Target);

        Assert.Equal(added, stacked);

        await session.MoveItemsAsync(Target, Other, Scrambled(stacked), Cancellation);

        Assert.Empty((await session.GetSceneAsync(Target, Cancellation)).Items);

        // A move keeps the ItemId, so the stack can be compared picture by picture.
        Assert.Equal(stacked, await ByDepth(session, Other));
    }

    /// <summary>The same for copying, where the ids are new and the assets carry the comparison.</summary>
    [Fact]
    public async Task A_copied_selection_lies_on_the_target_in_the_order_it_had_on_the_source()
    {
        using var session = Session(out var screens);
        screens.Report(Device, Two(), reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(5)], Cancellation);
        var assets = await AssetsByDepth(session, Target);

        var copies = await session.CopyItemsAsync(Target, Other, Scrambled(added), Cancellation);

        Assert.Equal(5, copies.Count);

        // The templates stay exactly as they were.
        Assert.Equal(assets, await AssetsByDepth(session, Target));
        Assert.Equal(assets, await AssetsByDepth(session, Other));
    }

    /// <summary>
    /// One patch over TWO screens, which is the shape the single form already had and the reason
    /// this could not simply reuse the per-screen collective machinery.
    /// </summary>
    [Fact]
    public async Task Moving_a_selection_is_one_patch_that_names_both_screens()
    {
        using var session = Session(out var screens);
        screens.Report(Device, Two(), reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(4)], Cancellation);

        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        await using (stream.ConfigureAwait(false))
        {
            Assert.True(await stream.MoveNextAsync());

            await session.MoveItemsAsync(Target, Other, added, Cancellation);

            Assert.True(await stream.MoveNextAsync());

            var patched = Assert.IsType<SessionEvent.ScenePatched>(stream.Current);

            // Four pictures, four departures and four arrivals, one patch.
            Assert.Equal(8, patched.Patch.Ops.Count);
            Assert.Equal(4, patched.Patch.Ops.Count(op => op.Screen == Target && op.Op is RemoveItem));
            Assert.Equal(4, patched.Patch.Ops.Count(op => op.Screen == Other && op.Op is AddItem));
        }
    }

    /// <summary>
    /// Moving onto the screen it already lies on does nothing, exactly as the single form has it -
    /// and it must not become a departure followed by an arrival, which would hand out new depths
    /// and fire the arrival highlight for pictures that went nowhere (Part 4).
    /// </summary>
    [Fact]
    public async Task Moving_a_selection_onto_its_own_screen_does_nothing()
    {
        using var session = Session(out var screens);
        screens.Report(Device, Two(), reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(3)], Cancellation);
        var stacked = await ByDepth(session, Target);

        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        await using (stream.ConfigureAwait(false))
        {
            Assert.True(await stream.MoveNextAsync());

            await session.MoveItemsAsync(Target, Target, added, Cancellation);

            // Something else afterwards, so the test cannot pass by reading nothing at all.
            await session.ToggleItemsAsync(Target, visible: false, Cancellation);

            Assert.True(await stream.MoveNextAsync());

            var patched = Assert.IsType<SessionEvent.ScenePatched>(stream.Current);

            _ = Assert.IsType<ToggleItems>(Assert.Single(patched.Patch.Ops).Op);
        }

        Assert.Equal(stacked, await ByDepth(session, Target));
    }

    /// <summary>
    /// <b>The same question of order one level in.</b> Coming back out of the fan hands out fresh
    /// depths, so the run's order becomes the stacking order on the table - and the order that
    /// belongs there is the fan's own, which the players could see, not the order the DM happened
    /// to tap the cards in.
    /// </summary>
    [Fact]
    public async Task A_selection_taken_out_of_the_fan_lands_in_the_fans_order_not_the_taps()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(5)], Cancellation);

        await session.ParkItemsAsync(Target, added, parked: true, Cancellation);

        IReadOnlyList<ItemId> fan =
        [
            .. (await session.GetSceneAsync(Target, Cancellation))
                .Items.OrderBy(item => item.ParkedAt).Select(item => item.ItemId),
        ];

        Assert.Equal(added, fan);

        await session.ParkItemsAsync(Target, Scrambled(fan), parked: false, Cancellation);

        Assert.Equal(fan, await ByDepth(session, Target));
    }

    /// <summary>
    /// <b>Away and back again is an identity, and it takes both keys to be one.</b> Each direction
    /// hands out a fresh order as the run proceeds - a place in the fan going in, a depth coming
    /// out - so each direction would otherwise write whatever order the selection happened to
    /// carry into what the players see.
    /// <para>
    /// The selection is handed over scrambled BOTH times, and differently each time, so that
    /// neither leg can pass by accident: with only the outbound key the fan would already hold the
    /// tap order, and the return leg would faithfully restore the wrong thing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_selection_put_away_and_fetched_back_lies_exactly_as_it_did()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        _ = await session.AddItemsAsync(Target, [.. Many(6)], Cancellation);

        var table = await ByDepth(session, Target);

        await session.ParkItemsAsync(Target, Scrambled(table), parked: true, Cancellation);

        IReadOnlyList<ItemId> fan =
        [
            .. (await session.GetSceneAsync(Target, Cancellation))
                .Items.OrderBy(item => item.ParkedAt).Select(item => item.ItemId),
        ];

        // The fan took over the stack as it lay, not the order the cards were tapped in.
        Assert.Equal(table, fan);

        // Scrambled the other way for the way back, so the return leg cannot ride on the first.
        await session.ParkItemsAsync(
            Target, [.. table.Skip(2), .. table.Take(2)], parked: false, Cancellation);

        Assert.Equal(table, await ByDepth(session, Target));
    }

    /// <summary>Turning a whole selection towards the DM is one command and therefore one patch.</summary>
    [Fact]
    public async Task Turning_a_selection_is_one_patch()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var added = await session.AddItemsAsync(Target, [.. Many(6)], Cancellation);
        var before = (await session.GetSceneAsync(Target, Cancellation)).Items;

        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        await using (stream.ConfigureAwait(false))
        {
            Assert.True(await stream.MoveNextAsync());

            await session.TransformItemsAsync(
                Target,
                [
                    .. before.Select(item => new ItemTransform(
                        item.ItemId, item.CenterX, item.CenterY, item.Scale, RotationDeg: 180)),
                ],
                toFront: false,
                Cancellation);

            Assert.True(await stream.MoveNextAsync());

            var patched = Assert.IsType<SessionEvent.ScenePatched>(stream.Current);

            Assert.Equal(6, patched.Patch.Ops.Count);
            Assert.All(patched.Patch.Ops, op => Assert.IsType<TransformItem>(op.Op));
        }

        var after = (await session.GetSceneAsync(Target, Cancellation)).Items;

        Assert.All(after, item => Assert.Equal(180, item.RotationDeg));

        // Turning is not taking hold of: nothing changed places in the stack.
        Assert.Equal(added, await ByDepth(session, Target));
    }

    private static IEnumerable<AssetRef> Many(int count) =>
        Enumerable.Range(0, count).Select(index => new AssetRef(
            new AssetId(index.ToString("x4", CultureInfo.InvariantCulture).PadLeft(64, 'd')),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "Grimmbart"));

    private static SessionApi Session(out ScreenCatalog screens)
    {
        var options = new HubOptions
        {
            KnownDevices = [new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "a-token")],
        };

        screens = new ScreenCatalog();

        return new SessionApi(
            new SceneStore(),
            screens,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(options), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);
    }

    private static ScreenInfo Info() => Info(Screen, "TISCH-PC//DISPLAY1", primary: true);

    private static ScreenInfo Info(ScreenId screen, string label, bool primary) =>
        new(screen, label, null, new PixelSize(1920, 1080), 96, primary);

    /// <summary>Both screens of a relocation.</summary>
    private static ScreenInfo[] Two() =>
        [Info(), Info(Second, "TISCH-PC//DISPLAY2", primary: false)];

    /// <summary>The ids of one screen's pictures, bottom of the stack first.</summary>
    private static async Task<IReadOnlyList<ItemId>> ByDepth(SessionApi session, ScreenRef screen) =>
        [
            .. (await session.GetSceneAsync(screen, Cancellation))
                .Items.OrderBy(item => item.ZOrder).Select(item => item.ItemId),
        ];

    /// <summary>The assets of one screen's pictures, bottom first - what a copy carries over.</summary>
    private static async Task<IReadOnlyList<AssetId>> AssetsByDepth(SessionApi session, ScreenRef screen) =>
        [
            .. (await session.GetSceneAsync(screen, Cancellation))
                .Items.OfType<ImageItem>().OrderBy(item => item.ZOrder).Select(item => item.AssetId),
        ];

    /// <summary>
    /// The same ids in an order that is deliberately NOT the stacking order - which is what a
    /// selection hands over, since it carries the order the DM picked in and the menu whatever
    /// order the scene happens to hold.
    /// </summary>
    private static IReadOnlyList<ItemId> Scrambled(IReadOnlyList<ItemId> ids) => [.. ids.Reverse()];
}
