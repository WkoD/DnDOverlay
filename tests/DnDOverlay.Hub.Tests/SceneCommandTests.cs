using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The commands M2b adds to <see cref="ISessionApi"/>, seen from the control side: each one changes
/// the authoritative scene <b>and</b> puts exactly one patch on the wire.
/// <para>
/// One command, one patch, never merged with the next over a time window - and the same patch to
/// both audiences, because a second control has to APPLY it rather than be handed a whole scene
/// (Part 4, rule 1).
/// </para>
/// </summary>
public sealed class SceneCommandTests
{
    private static readonly DeviceId Device = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly ScreenId Screen = new(@"\\?\DISPLAY#COMMANDS#1");
    private static readonly ScreenRef Target = new(Device, Screen);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Removing_an_item_takes_it_out_of_the_authoritative_scene()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.RemoveItemAsync(Target, item, Cancellation);

        Assert.Empty((await session.GetSceneAsync(Target, Cancellation)).Items);
    }

    /// <summary>
    /// <b>The seam nobody had crossed:</b> the hub really does place a new item through
    /// <c>Placement</c>, with the screen's own settings, and the result really does end up in the
    /// authoritative scene.
    /// <para>
    /// <c>AddItemAsync(position: null)</c> appears a dozen times in these tests and not one of them
    /// ever looked at WHERE the item landed. Placement itself is covered in <c>Core</c> against a
    /// screen built by hand - so both halves were green while the wiring between them was an
    /// assumption. That is the shape of fault this project has already paid for once, when a client
    /// and a hub were each tested against their own stand-in and neither sent a token.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_items_without_a_position_are_placed_apart_by_the_hub()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        var items = (await session.GetSceneAsync(Target, Cancellation)).Items.OfType<ImageItem>().ToList();

        Assert.Equal(2, items.Count);
        Assert.NotEqual(
            (items[0].CenterX, items[0].CenterY),
            (items[1].CenterX, items[1].CenterY));

        // Both on the screen, and both at the size a picture arrives in - the whole computation the
        // hub is supposed to have done, not merely "some number was written".
        Assert.All(items, item => Assert.InRange(item.CenterX, 0, 1));
        Assert.All(items, item => Assert.InRange(item.CenterY, 0, 1));
        Assert.All(items, item => Assert.True(item.Scale > 0));
    }

    /// <summary>
    /// And the placement MODE reaches it: the same two items under <c>Cascade</c> land somewhere
    /// else than under <c>Flow</c>. Without this the grip in the control could be switching a
    /// setting that never arrives - which is exactly what step 16 asks to see side by side.
    /// </summary>
    [Fact]
    public async Task The_placement_mode_of_the_screen_reaches_the_placement()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        var flowed = (await session.GetSceneAsync(Target, Cancellation)).Items.OfType<ImageItem>().Single();

        await session.ApplyConfigAsync(
            Device,
            new ConfigUpdate([new ScreenConfigUpdate(Screen, new ScreenSettings(Placement: PlacementMode.Cascade))]),
            Cancellation);

        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        var cascaded = (await session.GetSceneAsync(Target, Cancellation))
            .Items.OfType<ImageItem>().Last();

        Assert.NotEqual((flowed.CenterX, flowed.CenterY), (cascaded.CenterX, cascaded.CenterY));

        // Cascade steps out from the CENTRE, flow starts at the top left. Asserted as the direction
        // rather than as a coordinate, so the test says which mode ran rather than repeating its
        // arithmetic.
        Assert.True(cascaded.CenterX > flowed.CenterX, "the second item was not placed the cascade way");
    }

    /// <summary>
    /// A command that reaches the hub after the item is already gone is not an error - it simply
    /// does nothing (Part 11).
    /// </summary>
    [Fact]
    public async Task Removing_something_that_is_not_there_is_not_an_error()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        await session.RemoveItemAsync(Target, new ItemId(Guid.NewGuid()), Cancellation);

        Assert.Equal(item, Assert.Single((await session.GetSceneAsync(Target, Cancellation)).Items).ItemId);
    }

    /// <summary>
    /// The hub does the work and the finished layer travels, exactly as the finished item does for
    /// <c>AddItem</c> (Part 1, rule 2). Fit and offset start at their resting values.
    /// </summary>
    [Fact]
    public async Task Setting_a_background_puts_a_finished_layer_on_the_screen()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.SetBackgroundAsync(Target, Reference(), Cancellation);

        var background = (await session.GetSceneAsync(Target, Cancellation)).Background;

        Assert.NotNull(background);
        Assert.Equal(Reference().AssetId, background.AssetId);
        Assert.False(background.ShowName);

        // It arrives covering the screen. Since M4 that is a place and a size rather than a mode,
        // so the assertion is the one thing Cover promises: no edge is left uncovered.
        var rect = Layout.BackgroundRect(background, screens.ContextFor(Target));

        Assert.True(rect.Width >= 1 - 1e-9 && rect.Height >= 1 - 1e-9, "the fresh background left a gap");
        Assert.Equal(0, background.RotationDeg);
    }

    /// <summary>
    /// The grip the thumbnail got in M4c reaches the authoritative scene - and <b>the hub has the
    /// last word</b>, as it does for an item (rule 2): the scale is held between its bounds and the
    /// layer at the edge of the glass, whatever a control sends.
    /// </summary>
    [Fact]
    public async Task Moving_the_background_reaches_the_scene_and_is_held_at_the_edge()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.SetBackgroundAsync(Target, Reference(), Cancellation);
        await session.TransformBackgroundAsync(Target, new Point(0.62, 0.44), 1.4, 15, Cancellation);

        var moved = (await session.GetSceneAsync(Target, Cancellation)).Background;

        Assert.NotNull(moved);
        Assert.Equal(1.4, moved.Scale, 6);
        Assert.Equal(15, moved.RotationDeg, 6);

        // Where it was sent, unchanged. The layer goes where the DM puts it, exactly as a picture
        // does - the clamp that used to pull a covering background back to the edge is gone, on the
        // DM's own reason: what is not covered is transparent, and that is fine.
        Assert.Equal(0.62, moved.CenterX, 6);
        Assert.Equal(0.44, moved.CenterY, 6);

        await session.TransformBackgroundAsync(Target, new Point(12, 12), 1.4, 0, Cancellation);

        var pushed = (await session.GetSceneAsync(Target, Cancellation)).Background;

        Assert.NotNull(pushed);

        // And the hub still has the last word (rule 2): the same clamp a picture gets keeps the
        // layer on the glass, so a control sending nonsense cannot lose it. The rule is asserted
        // rather than a coordinate - a number would say this today and break with the next change.
        var rect = Layout.BackgroundRect(pushed, screens.ContextFor(Target));

        Assert.True(
            rect.Right > 0 && rect.X < 1 && rect.Bottom > 0 && rect.Y < 1,
            "the background was pushed clean off the glass and cannot be got back");
    }

    /// <summary>
    /// <b>The layer remembers how it was last arranged, and only a movement forgets it.</b> The
    /// menu ticks one of three - fill, fit, customize - so the DM can see at a glance what he set
    /// this to (07.09.2026). Switching the hand mode on and putting a finger down reports a step of
    /// nothing, and losing the answer by merely opening the menu would be the opposite of what the
    /// tick is for.
    /// </summary>
    [Fact]
    public async Task The_background_remembers_its_arrangement_until_it_is_actually_moved()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.SetBackgroundAsync(Target, Reference(), Cancellation);
        await session.SetBackgroundFitAsync(Target, BackgroundFit.Cover, Cancellation);

        var filled = (await session.GetSceneAsync(Target, Cancellation)).Background;

        Assert.NotNull(filled);
        Assert.Equal(BackgroundFit.Cover, filled.Fit);

        // A gesture that reports the values it already has - a finger down and up again.
        await session.TransformBackgroundAsync(
            Target,
            new Point(filled.CenterX, filled.CenterY),
            filled.Scale,
            filled.RotationDeg,
            Cancellation);

        var untouched = (await session.GetSceneAsync(Target, Cancellation)).Background;

        Assert.NotNull(untouched);
        Assert.Equal(BackgroundFit.Cover, untouched.Fit);

        // And one that really moves it.
        await session.TransformBackgroundAsync(
            Target,
            new Point(filled.CenterX + 0.1, filled.CenterY),
            filled.Scale,
            filled.RotationDeg,
            Cancellation);

        var moved = (await session.GetSceneAsync(Target, Cancellation)).Background;

        Assert.NotNull(moved);
        Assert.Null(moved.Fit);

        // Back into an arrangement, and it is remembered again - so the tick can come back.
        await session.SetBackgroundFitAsync(Target, BackgroundFit.Contain, Cancellation);

        Assert.Equal(
            BackgroundFit.Contain,
            (await session.GetSceneAsync(Target, Cancellation)).Background?.Fit);
    }

    /// <summary>
    /// Without a background it does nothing rather than failing - the same rule an unknown ItemId
    /// follows (Part 11). The menu entry is disabled, and a second control need not know that.
    /// </summary>
    [Fact]
    public async Task Moving_a_background_that_is_not_there_does_nothing()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.TransformBackgroundAsync(Target, new Point(0.3, 0.3), 1, 0, Cancellation);

        Assert.Null((await session.GetSceneAsync(Target, Cancellation)).Background);
    }

    /// <summary>Strictly separate from the items - which is why "empty the lot" has to send both.</summary>
    [Fact]
    public async Task Clearing_the_background_leaves_the_items_standing()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.SetBackgroundAsync(Target, Reference(), Cancellation);
        await session.ClearBackgroundAsync(Target, Cancellation);

        var scene = await session.GetSceneAsync(Target, Cancellation);

        Assert.Null(scene.Background);
        Assert.Equal(item, Assert.Single(scene.Items).ItemId);
    }

    /// <summary>
    /// One picture, one name (Part 3): renaming the asset reaches every item carrying it, and the
    /// background too when it shows the same picture.
    /// </summary>
    [Fact]
    public async Task Renaming_an_asset_reaches_everything_showing_it()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.SetBackgroundAsync(Target, Reference(), Cancellation);

        await session.SetAssetNameAsync(Target, Reference().AssetId, "Ratsherr Vellin", Cancellation);

        var scene = await session.GetSceneAsync(Target, Cancellation);

        Assert.All(scene.Items.OfType<ImageItem>(), item => Assert.Equal("Ratsherr Vellin", item.Name));
        Assert.Equal("Ratsherr Vellin", scene.Background!.Name);
    }

    /// <summary>The caption belongs to the INSTANCE, so it reaches exactly one item.</summary>
    [Fact]
    public async Task Showing_a_name_reaches_one_item_and_not_its_twin()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var wanted = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        var other = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);

        await session.SetShowNameAsync(Target, wanted, show: true, Cancellation);

        var items = (await session.GetSceneAsync(Target, Cancellation)).Items.OfType<ImageItem>().ToList();

        Assert.True(items.Single(item => item.ItemId == wanted).ShowName);
        Assert.False(items.Single(item => item.ItemId == other).ShowName);
    }

    /// <summary>No item named means the background layer - a city map wants its name (Part 7).</summary>
    [Fact]
    public async Task Showing_a_name_without_an_item_means_the_background()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        await session.SetBackgroundAsync(Target, Reference(), Cancellation);
        await session.SetShowNameAsync(Target, item: null, show: true, Cancellation);
        await session.SetAnimationPausedAsync(Target, item: null, paused: true, Cancellation);

        var background = (await session.GetSceneAsync(Target, Cancellation)).Background;

        Assert.True(background!.ShowName);
        Assert.True(background.AnimationPaused);
    }

    /// <summary>
    /// Hiding is not deleting. The pictures stay in the scene and in the device's store, which is
    /// what makes fading them back in immediate and free of a second transfer (Part 7, step 24).
    /// </summary>
    [Fact]
    public async Task Switching_a_layer_off_keeps_what_is_on_it()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
        await session.SetBackgroundAsync(Target, Reference(), Cancellation);

        await session.ToggleItemsAsync(Target, visible: false, Cancellation);
        await session.ToggleBackgroundAsync(Target, visible: false, Cancellation);

        var scene = await session.GetSceneAsync(Target, Cancellation);

        Assert.False(scene.ItemsVisible);
        Assert.False(scene.BackgroundVisible);
        Assert.Equal(item, Assert.Single(scene.Items).ItemId);
        Assert.NotNull(scene.Background);
    }

    /// <summary>
    /// Every one of them puts its patch on the stream, addressed at the screen it was aimed at.
    /// This is the half that says the DEVICES and a second control learn of it at all - a change
    /// that only reached the store would be a table that never moved.
    /// </summary>
    /// <remarks>
    /// A timeout, and it was earned: with the dispatch taken out this test did not fail, it
    /// <b>hung</b> - the read simply waits for an event that never comes. A hanging test says the
    /// same thing as a failing one and says it an hour later.
    /// </remarks>
    [Fact(Timeout = 30_000)]
    public async Task Every_command_puts_exactly_one_addressed_patch_on_the_stream()
    {
        var cancellation = TestContext.Current.CancellationToken;

        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var item = await session.AddItemAsync(Target, Reference(), position: null, cancellation);

        await using var stream = session.Subscribe(cancellation).GetAsyncEnumerator(cancellation);
        Assert.True(await stream.MoveNextAsync());

        var commands = new (string Name, Func<Task> Run)[]
        {
            ("RemoveItem", () => session.RemoveItemAsync(Target, item, cancellation)),
            ("SetBackground", () => session.SetBackgroundAsync(Target, Reference(), cancellation)),
            ("SetName", () => session.SetAssetNameAsync(Target, Reference().AssetId, "Vellin", cancellation)),
            ("SetShowName", () => session.SetShowNameAsync(Target, null, true, cancellation)),
            ("SetAnimationPaused", () => session.SetAnimationPausedAsync(Target, null, true, cancellation)),
            ("ToggleItems", () => session.ToggleItemsAsync(Target, false, cancellation)),
            ("ToggleBackground", () => session.ToggleBackgroundAsync(Target, false, cancellation)),
            ("ClearBackground", () => session.ClearBackgroundAsync(Target, cancellation)),
        };

        foreach (var (name, run) in commands)
        {
            await run();

            Assert.True(await stream.MoveNextAsync(), $"{name} put nothing on the stream");

            var patched = Assert.IsType<SessionEvent.ScenePatched>(stream.Current);
            var op = Assert.Single(patched.Patch.Ops);

            Assert.Equal(Target, op.Screen);
        }
    }

    /// <summary>
    /// <b>The seam the control fell through.</b> A run of commands larger than a subscriber's queue
    /// ends that subscriber's stream - proved in <c>SessionStreamTests</c> against the raw fan-out,
    /// and here against the real command path, because that is where it actually happened: an
    /// intake of 723 files sent 714 <c>AddItem</c>s into a queue of 256, from the very thread that
    /// had to read them (07.09.2026).
    /// <para>
    /// The half that had never been crossed is the way back. The rule says a cut subscriber
    /// subscribes again for a fresh opening picture - and this pins what that picture is and is
    /// NOT: a new stream, yes, but <b>the scene is not in it</b>. Everything the cut swallowed is
    /// in the authoritative scene and has to be fetched. A control that only re-subscribed would
    /// come back to a live stream and a stale table, which looks like recovery and is not.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_run_larger_than_the_queue_ends_the_stream_and_leaves_the_scene_whole()
    {
        using var session = Session(out var screens);
        screens.Report(Device, [Info()], reported: null);

        var stream = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        await using (stream.ConfigureAwait(false))
        {
            Assert.True(await stream.MoveNextAsync());
            Assert.IsType<SessionEvent.Opening>(stream.Current);

            const int Run = SessionEvents.Capacity + 44;

            for (var i = 0; i < Run; i++)
            {
                await session.AddItemAsync(Target, Reference(), position: null, Cancellation);
            }

            var seen = 0;

            while (await stream.MoveNextAsync())
            {
                seen++;
            }

            // What fitted, and then the stream is over - not the run.
            Assert.Equal(SessionEvents.Capacity, seen);

            // Nothing was lost where it counts: the hub holds every one of them.
            Assert.Equal(Run, (await session.GetSceneAsync(Target, Cancellation)).Items.Count);
        }

        var again = session.Subscribe(Cancellation).GetAsyncEnumerator(Cancellation);

        await using (again.ConfigureAwait(false))
        {
            Assert.True(await again.MoveNextAsync());

            // A fresh stream, and an opening picture about DEVICES. The table is not in it.
            _ = Assert.IsType<SessionEvent.Opening>(again.Current);
        }
    }

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

    private static ScreenInfo Info() =>
        new(Screen, "TISCH-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, true);

    private static AssetRef Reference() =>
        new(
            new AssetId(new string('d', 64)),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "Grimmbart");
}
