using DnDOverlay.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// Moving and copying between screens - the two commands M4 adds, and the two that four promises in
/// Part 11 had been resting on since M1a without anything being built.
/// <para>
/// Both are commands rather than patch operations, because Part 11 prescribes the decomposition
/// itself: a move is a <c>RemoveItem</c> plus an <c>AddItem</c> under the same id, a copy is an
/// <c>AddItem</c> with a new one. An operation of their own would be a second road to the same
/// state, and the arrival rule would have to learn it twice.
/// </para>
/// </summary>
public sealed class MoveAndCopyTests
{
    private static readonly DeviceId Device = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000007"));
    private static readonly ScreenRef Table = new(Device, new ScreenId(@"\\?\DISPLAY#MOVE#1"));
    private static readonly ScreenRef Beamer = new(Device, new ScreenId(@"\\?\DISPLAY#MOVE#2"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// The shape of the patch, which is the whole promise: one patch, two operations, the same id -
    /// so no display ever sees a table without the picture and none sees it twice (Part 4).
    /// </summary>
    [Fact]
    public async Task A_move_is_one_patch_that_removes_and_adds_under_the_same_id()
    {
        var events = new SessionEvents();
        var session = Session(events, out _);

        var item = await session.AddItemAsync(Table, Reference(), position: null, Cancellation);

        using var subscription = events.Open(() => new SessionEvent.DevicesChanged([]));

        await session.MoveItemAsync(Table, Beamer, item, position: null, Cancellation);

        var patch = Assert.IsType<SessionEvent.ScenePatched>(await NextAfterOpeningAsync(subscription)).Patch;

        Assert.Collection(
            patch.Ops,
            op =>
            {
                Assert.Equal(Table, op.Screen);
                Assert.Equal(item, Assert.IsType<RemoveItem>(op.Op).Item);
            },
            op =>
            {
                Assert.Equal(Beamer, op.Screen);
                Assert.Equal(item, Assert.IsType<AddItem>(op.Op).Item.ItemId);
            });

        Assert.Empty((await session.GetSceneAsync(Table, Cancellation)).Items);
        Assert.Equal(item, (await session.GetSceneAsync(Beamer, Cancellation)).Items.Single().ItemId);
    }

    /// <summary>
    /// Normalised coordinates make a move "a plain re-hanging of the ScreenId" (Part 3): without an
    /// aimed drop point the picture keeps its place, and it arrives on top because arriving counts
    /// as being touched (Part 3, the fourth of the five ZOrder triggers).
    /// </summary>
    [Fact]
    public async Task A_moved_item_keeps_its_place_and_arrives_on_top()
    {
        var session = Session(out _);

        var travelling = await session.AddItemAsync(Table, Reference(), position: null, Cancellation);

        await session.TransformItemAsync(
            Table,
            new ItemTransform(travelling, 0.2, 0.8, 0.3, 45),
            fromTable: true,
            toFront: false,
            Cancellation);

        // Two pictures already lying on the target, so "on top" is a statement about its stack.
        await session.AddItemAsync(Beamer, Reference(), position: null, Cancellation);
        await session.AddItemAsync(Beamer, Reference(), position: null, Cancellation);

        var before = await session.GetSceneAsync(Beamer, Cancellation);

        await session.MoveItemAsync(Table, Beamer, travelling, position: null, Cancellation);

        var arrived = (await session.GetSceneAsync(Beamer, Cancellation)).Items
            .Single(item => item.ItemId == travelling);

        Assert.Equal(0.2, arrived.CenterX, precision: 9);
        Assert.Equal(0.8, arrived.CenterY, precision: 9);
        Assert.Equal(45, arrived.RotationDeg);
        Assert.True(arrived.ZOrder > before.Items.Max(item => item.ZOrder), "it did not arrive on top");
    }

    /// <summary>An aimed drop point wins - the DM let go somewhere, and that is where it goes.</summary>
    [Fact]
    public async Task An_aimed_drop_point_wins_over_the_place_it_had()
    {
        var session = Session(out _);

        var item = await session.AddItemAsync(Table, Reference(), position: null, Cancellation);

        await session.MoveItemAsync(Table, Beamer, item, new Point(0.75, 0.25), Cancellation);

        var arrived = (await session.GetSceneAsync(Beamer, Cancellation)).Items.Single();

        Assert.Equal(0.75, arrived.CenterX, precision: 9);
        Assert.Equal(0.25, arrived.CenterY, precision: 9);
    }

    /// <summary>
    /// "The same cap bites when moving onto a target of a different aspect ratio" (Part 3). Checked
    /// the way the promise is phrased - the picture FITS the width - rather than against the
    /// formula, which would only prove that the test can multiply the way the code does.
    /// </summary>
    [Fact]
    public async Task A_panorama_is_capped_again_on_a_screen_of_another_shape()
    {
        var session = Session(out var screens);
        screens.Report(Device, [Landscape(), Portrait()], reported: null);

        var item = await session.AddItemAsync(Table, Panorama(), position: null, Cancellation);

        var wide = (await session.GetSceneAsync(Table, Cancellation)).Items.Single();

        Assert.True(
            Layout.ItemToRect(wide, screens.ContextFor(Table)).Width <= 1.0,
            "it did not even fit where it was added");

        await session.MoveItemAsync(Table, Beamer, item, position: null, Cancellation);

        var moved = (await session.GetSceneAsync(Beamer, Cancellation)).Items.Single();

        Assert.True(moved.Scale < wide.Scale, "the cap did nothing on a screen half as wide");
        Assert.True(
            Layout.ItemToRect(moved, screens.ContextFor(Beamer)).Width <= 1.0,
            "the panorama arrived wider than the screen");
    }

    /// <summary>
    /// <b>A parked picture arrives lying free</b>, like a copy - and the target's fan is left as it
    /// was.
    /// <para>
    /// Part 11 said the opposite until the hand-run of M4 (step 25b): parked and into the target's
    /// fan. At the table that turned out to be the wrong answer to a plain question - a picture
    /// dragged onto another screen is one that is WANTED there, and it arrived where nobody was
    /// looking. The plan is corrected with this test rather than beside it.
    /// </para>
    /// <para>
    /// The fan of the target is asserted as an INVARIANT rather than a coordinate: laying it out
    /// again must change nothing. Written against a number it would say the same thing today and
    /// break with the next change to the fan.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_parked_item_arrives_lying_free()
    {
        var session = Session(out var screens);
        screens.Report(Device, [Landscape(), Portrait()], reported: null);

        var item = await session.AddItemAsync(Table, Reference(), position: null, Cancellation);
        await session.ParkItemAsync(Table, item, parked: true, Cancellation);

        // One already lying in the target's fan, so the arrival has an order to join. It was parked
        // BEFORE the traveller, and it still ends up behind it: the arriving picture is the newest
        // in this fan, which is what a fresh ParkedAt says.
        var resident = await session.AddItemAsync(Beamer, Reference(), position: null, Cancellation);
        await session.ParkItemAsync(Beamer, resident, parked: true, Cancellation);

        await session.MoveItemAsync(Table, Beamer, item, position: null, Cancellation);

        var scene = await session.GetSceneAsync(Beamer, Cancellation);
        var arrived = scene.Items.Single(candidate => candidate.ItemId == item);

        Assert.False(arrived.Parked, "it went into the target's fan instead of onto the table");
        Assert.Equal(0, arrived.ParkedAt);

        // The one card that was already put away is still put away, and alone in the fan.
        Assert.Equal([resident], Parking.Fan(scene).Select(card => card.ItemId));
        Assert.Equal(scene, Parking.Arrange(scene, screens.ContextFor(Beamer)));
    }

    /// <summary>
    /// Two ways of asking for nothing, and both answer with nothing: an id that is not there
    /// (Part 11 - ineffective rather than an error) and a target that is the source. Dragging inside
    /// one tile is a transform; a move would hand out a new ZOrder and light up the arrival
    /// highlight for a picture that never went anywhere.
    /// </summary>
    [Fact]
    public async Task A_move_that_asks_for_nothing_does_nothing()
    {
        var session = Session(out _);

        var item = await session.AddItemAsync(Table, Reference(), position: null, Cancellation);
        var before = await session.GetSceneAsync(Table, Cancellation);

        await session.MoveItemAsync(Table, Beamer, new ItemId(Guid.NewGuid()), position: null, Cancellation);
        await session.MoveItemAsync(Table, Table, item, position: null, Cancellation);

        Assert.Equal(before, await session.GetSceneAsync(Table, Cancellation));
        Assert.Empty((await session.GetSceneAsync(Beamer, Cancellation)).Items);
    }

    /// <summary>
    /// The copy is its own picture on the same asset: new id, size and angle taken over, a new
    /// ZOrder - and the template untouched, which is the half a copy quietly gets wrong (Part 11).
    /// </summary>
    [Fact]
    public async Task A_copy_gets_a_new_id_and_leaves_its_template_alone()
    {
        var session = Session(out _);

        var template = await session.AddItemAsync(Table, Reference(), position: null, Cancellation);

        await session.TransformItemAsync(
            Table,
            new ItemTransform(template, 0.4, 0.6, 0.28, 30),
            fromTable: true,
            toFront: false,
            Cancellation);

        var before = (await session.GetSceneAsync(Table, Cancellation)).Items.Single();

        var copy = await session.CopyItemAsync(Table, Beamer, template, position: null, Cancellation);

        Assert.NotNull(copy);
        Assert.NotEqual(template, copy);

        var made = (await session.GetSceneAsync(Beamer, Cancellation)).Items.Single();

        Assert.Equal(copy, made.ItemId);
        Assert.Equal(before.Scale, made.Scale, precision: 9);
        Assert.Equal(before.RotationDeg, made.RotationDeg);
        Assert.Equal(((ImageItem)before).AssetId, ((ImageItem)made).AssetId);

        Assert.Equal(before, (await session.GetSceneAsync(Table, Cancellation)).Items.Single());
    }

    /// <summary>
    /// A copy onto the screen it came from is the case the DM uses for a second guard, and it has to
    /// be visible as two pictures: it lands beside its template, not on it (Part 11).
    /// </summary>
    [Fact]
    public async Task A_copy_onto_the_same_screen_lands_beside_its_template()
    {
        var session = Session(out _);

        var template = await session.AddItemAsync(Table, Reference(), position: null, Cancellation);
        var before = (await session.GetSceneAsync(Table, Cancellation)).Items.Single();

        var copy = await session.CopyItemAsync(Table, Table, template, position: null, Cancellation);

        var scene = await session.GetSceneAsync(Table, Cancellation);
        var made = scene.Items.Single(item => item.ItemId == copy);

        Assert.Equal(2, scene.Items.Count);
        Assert.True(
            Math.Abs(made.CenterX - before.CenterX) > 1e-9 || Math.Abs(made.CenterY - before.CenterY) > 1e-9,
            "the copy landed exactly on its template");
        Assert.True(made.ZOrder > before.ZOrder, "the copy did not land on top");
    }

    /// <summary>
    /// Parking is where a picture was put away; a copy of it is one that is wanted now. It therefore
    /// lies free, and its place comes from the screen's placement mode - the template's own place is
    /// a slot in the fan and means nothing outside it (Part 11).
    /// </summary>
    [Fact]
    public async Task A_copy_of_a_parked_template_lies_free()
    {
        var session = Session(out var screens);

        var template = await session.AddItemAsync(Table, Reference(), position: null, Cancellation);
        await session.ParkItemAsync(Table, template, parked: true, Cancellation);

        var copy = await session.CopyItemAsync(Table, Table, template, position: null, Cancellation);

        var scene = await session.GetSceneAsync(Table, Cancellation);
        var made = scene.Items.Single(item => item.ItemId == copy);

        Assert.False(made.Parked, "the copy went straight into the fan");
        Assert.Equal([template], Parking.Fan(scene).Select(card => card.ItemId));

        // And it lies where a new picture would lie, not where the fan had put its template.
        var expected = Placement.NextPosition(
            scene with { Items = [.. scene.Items.Where(item => item.ItemId != copy)] },
            made.Scale,
            made.AspectRatio,
            screens.ContextFor(Table));

        Assert.Equal(expected.X, made.CenterX, precision: 9);
        Assert.Equal(expected.Y, made.CenterY, precision: 9);
    }

    private static async Task<SessionEvent> NextAfterOpeningAsync(SessionEvents.Subscription subscription)
    {
        await using var stream = subscription
            .ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await stream.MoveNextAsync());
        Assert.True(await stream.MoveNextAsync());

        return stream.Current;
    }

    private static SessionApi Session(out ScreenCatalog screens) => Session(new SessionEvents(), out screens);

    private static SessionApi Session(SessionEvents events, out ScreenCatalog screens)
    {
        var options = new HubOptions
        {
            KnownDevices = [new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "a-token")],
        };

        screens = new ScreenCatalog();
        screens.Report(Device, [Landscape(), Wall()], reported: null);

        return new SessionApi(
            new SceneStore(),
            screens,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(options), TimeProvider.System),
            events,
            null,
            NullLogger<SessionApi>.Instance);
    }

    private static ScreenInfo Landscape() =>
        new(Table.Screen, "TISCH-PC//TABLE", null, new PixelSize(1920, 1080), 96, true);

    private static ScreenInfo Wall() =>
        new(Beamer.Screen, "TISCH-PC//BEAMER", null, new PixelSize(1920, 1080), 96, true);

    private static ScreenInfo Portrait() =>
        new(Beamer.Screen, "TISCH-PC//BEAMER", null, new PixelSize(1080, 1920), 96, true);

    private static AssetRef Reference() =>
        new(
            new AssetId(new string('d', 64)),
            new AssetMeta(800, 600, "png", 1024, false, new string('c', 64)),
            "Grimmbart");

    private static AssetRef Panorama() =>
        new(
            new AssetId(new string('e', 64)),
            new AssetMeta(5000, 500, "png", 4096, false, new string('f', 64)),
            "Kartenstreifen");
}
