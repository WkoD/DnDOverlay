using System.Globalization;
using DnDOverlay.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The commands M5b adds before there is a timeline to undo them: the focus, clearing one screen,
/// "clear all" and "send to all" (Part 3, Part 4, Part 7). Each is one patch, however many screens
/// and pictures it touches.
/// </summary>
public sealed class FocusAndClearCommandTests
{
    private static readonly DeviceId Device = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"));
    private static readonly ScreenRef First = new(Device, new ScreenId(@"\\?\DISPLAY#FOCUS#1"));
    private static readonly ScreenRef Second = new(Device, new ScreenId(@"\\?\DISPLAY#FOCUS#2"));
    private static readonly ScreenRef Third = new(Device, new ScreenId(@"\\?\DISPLAY#FOCUS#3"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_focus_lies_on_the_chosen_items_in_the_order_they_were_chosen()
    {
        using var session = Session();
        var items = await session.AddItemsAsync(First, [.. Many(4)], Cancellation);

        var chosen = new[] { items[2], items[0], items[3] };

        Assert.Equal(FocusOutcome.Set, await session.SetFocusAsync(First, chosen, Cancellation));
        Assert.Equal(chosen, (await session.GetSceneAsync(First, Cancellation)).FocusItems);
    }

    /// <summary>Focus is a way of drawing: the items are field for field what they were.</summary>
    [Fact]
    public async Task A_focus_changes_nothing_about_the_items()
    {
        using var session = Session();
        var items = await session.AddItemsAsync(First, [.. Many(3)], Cancellation);
        var before = (await session.GetSceneAsync(First, Cancellation)).Items;

        await session.SetFocusAsync(First, [items[1]], Cancellation);

        Assert.Equal(before, (await session.GetSceneAsync(First, Cancellation)).Items);
    }

    /// <summary>Above the limit it is a second scene, not a highlight - refused, and nothing changes.</summary>
    [Fact]
    public async Task More_than_the_limit_is_refused_and_changes_nothing()
    {
        using var session = Session();
        var items = await session.AddItemsAsync(First, [.. Many(Focus.Limit + 1)], Cancellation);

        Assert.Equal(FocusOutcome.TooMany, await session.SetFocusAsync(First, items, Cancellation));
        Assert.Empty((await session.GetSceneAsync(First, Cancellation)).FocusItems);
    }

    [Fact]
    public async Task An_empty_list_lifts_the_focus_and_unknown_items_do_not_set_one()
    {
        using var session = Session();
        var items = await session.AddItemsAsync(First, [.. Many(2)], Cancellation);

        await session.SetFocusAsync(First, items, Cancellation);

        Assert.Equal(FocusOutcome.Cleared, await session.SetFocusAsync(First, [], Cancellation));
        Assert.Empty((await session.GetSceneAsync(First, Cancellation)).FocusItems);
        Assert.Equal(
            FocusOutcome.NothingThere,
            await session.SetFocusAsync(First, [new ItemId(Guid.NewGuid())], Cancellation));
    }

    /// <summary>
    /// "Clear all" takes items and background off every played screen in one patch - and leaves a
    /// screen being prepared as inactive alone, because it is not part of the table.
    /// </summary>
    [Fact]
    public async Task Clear_all_clears_every_played_screen_in_one_patch_and_leaves_an_inactive_one()
    {
        using var session = Session();

        await session.AddItemsAsync(First, [.. Many(2)], Cancellation);
        await session.SetBackgroundAsync(First, Many(1).Single(), Cancellation);
        await session.AddItemsAsync(Third, [.. Many(1)], Cancellation);
        await session.SetScreenStateAsync(Third, ScreenState.Inactive, Cancellation);

        var patches = await PatchesDuring(session, () => session.ClearAllAsync(Cancellation));

        var patch = Assert.Single(patches);

        Assert.Equal([typeof(ClearItems), typeof(ClearBackground)], patch.Ops.Select(op => op.Op.GetType()));
        Assert.All(patch.Ops, op => Assert.Equal(First, op.Screen));

        var first = await session.GetSceneAsync(First, Cancellation);

        Assert.Empty(first.Items);
        Assert.Null(first.Background);
        Assert.Single((await session.GetSceneAsync(Third, Cancellation)).Items);
    }

    /// <summary>Locked and parked pictures go too: a cleared screen with something left at the edge would be a nasty surprise.</summary>
    [Fact]
    public async Task Clearing_a_screen_takes_locked_and_parked_pictures_too()
    {
        using var session = Session();
        var items = await session.AddItemsAsync(First, [.. Many(3)], Cancellation);

        await session.SetLockedAsync(First, items[0], locked: true, Cancellation);
        await session.ParkItemAsync(First, items[1], parked: true, Cancellation);

        await session.ClearItemsAsync(First, Cancellation);

        Assert.Empty((await session.GetSceneAsync(First, Cancellation)).Items);
    }

    [Fact]
    public async Task Send_to_all_puts_the_pictures_on_every_played_screen_in_one_patch()
    {
        using var session = Session();

        await session.SetScreenStateAsync(Third, ScreenState.Inactive, Cancellation);

        IReadOnlyDictionary<ScreenRef, IReadOnlyList<ItemId>> arrived = new Dictionary<ScreenRef, IReadOnlyList<ItemId>>();

        var patches = await PatchesDuring(
            session,
            async () => arrived = await session.SendToAllAsync([.. Many(2)], Cancellation));

        var patch = Assert.Single(patches);

        Assert.Equal(4, patch.Ops.Count);
        Assert.Equal(2, arrived[First].Count);
        Assert.Equal(2, arrived[Second].Count);
        Assert.False(arrived.ContainsKey(Third));
        Assert.Equal(2, (await session.GetSceneAsync(Second, Cancellation)).Items.Count);
        Assert.Empty((await session.GetSceneAsync(Third, Cancellation)).Items);
    }

    /// <summary>
    /// A scene replacing a full screen is one patch - cleared and laid together, so no empty
    /// screen is seen between - and the items it lays are new, in the saved order.
    /// </summary>
    [Fact]
    public async Task Loading_a_scene_over_a_full_screen_is_one_patch_with_new_items()
    {
        using var session = Session();
        var lying = await session.AddItemsAsync(First, [.. Many(2)], Cancellation);
        var saved = await session.GetSceneAsync(First, Cancellation);

        IReadOnlyList<ItemId> arrived = [];

        var patches = await PatchesDuring(
            session,
            async () => arrived = await session.LoadSceneAsync(First, saved, SceneLoad.Replace, Cancellation));

        var patch = Assert.Single(patches);

        Assert.IsType<ClearItems>(patch.Ops[0].Op);
        Assert.Equal(2, arrived.Count);
        Assert.Empty(arrived.Intersect(lying));
        Assert.Equal(arrived, (await session.GetSceneAsync(First, Cancellation)).Items.OrderBy(item => item.ZOrder).Select(item => item.ItemId));
    }

    [Fact]
    public async Task Clearing_an_empty_table_sends_nothing()
    {
        using var session = Session();

        Assert.Empty(await PatchesDuring(session, () => session.ClearAllAsync(Cancellation)));
    }

    /// <summary>The patches published while one command ran - read from the event stream every surface reads.</summary>
    private static async Task<IReadOnlyList<ScenePatch>> PatchesDuring(SessionApi session, Func<Task> command)
    {
        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);
        var patches = new List<ScenePatch>();

        await using (stream.ConfigureAwait(false))
        {
            Assert.True(await stream.MoveNextAsync());

            await command();

            // A marker that is certain to come after whatever the command published.
            await session.ToggleItemsAsync(Second, visible: false, Cancellation);

            while (await stream.MoveNextAsync())
            {
                if (stream.Current is SessionEvent.ScenePatched patched)
                {
                    if (patched.Patch.Ops.Any(op => op.Op is ToggleItems))
                    {
                        break;
                    }

                    patches.Add(patched.Patch);
                }
            }
        }

        return patches;
    }

    private static IEnumerable<AssetRef> Many(int count) =>
        Enumerable.Range(0, count).Select(index => new AssetRef(
            new AssetId(index.ToString("x4", CultureInfo.InvariantCulture).PadLeft(64, 'e')),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "Söldner"));

    private static SessionApi Session()
    {
        var options = new HubOptions
        {
            KnownDevices = [new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "a-token")],
        };

        var screens = new ScreenCatalog();

        screens.Report(
            Device,
            [Info(First.Screen, "TISCH-PC//1"), Info(Second.Screen, "TISCH-PC//2"), Info(Third.Screen, "TISCH-PC//3")],
            reported: null);

        return new SessionApi(
            new SceneStore(),
            screens,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(options), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);
    }

    private static ScreenInfo Info(ScreenId screen, string label) =>
        new(screen, label, null, new PixelSize(1920, 1080), 96, IsPrimary: false);
}
