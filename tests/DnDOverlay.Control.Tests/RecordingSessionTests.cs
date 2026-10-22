using DnDOverlay.Core;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The timeline in front of the hub (siting question 3): which commands become a step, and that a
/// step is taken back surgically. Against the real hub session, so the patches are the real ones.
/// </summary>
public sealed class RecordingSessionTests
{
    private static readonly ScreenRef Screen = new(new DeviceId(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000c1")), new ScreenId("DISPLAY1"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Adding_a_picture_is_one_step_and_undo_takes_it_off_again()
    {
        using var session = Recording();

        await session.AddItemAsync(Screen, Picture("Grimmbart"), position: null, Cancellation);

        Assert.Equal("Add \"Grimmbart\"", session.Timeline.UndoLabel);

        Assert.Equal("Add \"Grimmbart\"", await session.UndoAsync());
        Assert.Empty((await session.GetSceneAsync(Screen, Cancellation)).Items);
        Assert.Equal("Add \"Grimmbart\"", session.Timeline.RedoLabel);
    }

    /// <summary>Locking is not an act that lays or takes a picture - it passes through without a step (Part 7).</summary>
    [Fact]
    public async Task Unlocking_all_is_not_a_step()
    {
        using var session = Recording();

        await session.AddItemAsync(Screen, Picture("Grimmbart"), position: null, Cancellation);
        await session.UnlockAllAsync(Screen, Cancellation);

        Assert.Equal("Add \"Grimmbart\"", session.Timeline.UndoLabel);
    }

    /// <summary>
    /// "Krieger" laid and taken off, then renamed to "Grimmbart" in the stock: undo brings it back as
    /// "Grimmbart" - one picture, one name (Part 3) - not under the name the step recorded.
    /// </summary>
    [Fact]
    public async Task Undo_after_a_rename_brings_the_picture_back_under_its_new_name()
    {
        using var session = Recording();
        var picture = Picture("Krieger");

        var item = await session.AddItemAsync(Screen, picture, position: null, Cancellation);
        await session.RemoveItemAsync(Screen, item, Cancellation);
        await session.RenameAssetAsync(picture.AssetId, "Grimmbart", Cancellation);
        await session.UndoAsync();

        var back = Assert.IsType<ImageItem>(Assert.Single((await session.GetSceneAsync(Screen, Cancellation)).Items));

        Assert.Equal("Grimmbart", back.Name);
    }

    /// <summary>
    /// A saved scene loaded from the tab names itself in the step (Part 7: "'Ratsversammlung' loaded")
    /// - two loads onto the same screen would otherwise read alike, and the DM could not tell which
    /// one the next press takes back.
    /// </summary>
    [Fact]
    public async Task A_loaded_scene_names_itself_in_the_step()
    {
        using var session = Recording();

        await session.AddItemAsync(Screen, Picture("Grimmbart"), position: null, Cancellation);

        var scene = await session.GetSceneAsync(Screen, Cancellation);

        await session.ClearItemsAsync(Screen, Cancellation);
        await session.LoadSavedSceneAsync("Ratsversammlung", Screen, scene, SceneLoad.Replace);

        Assert.Equal(Strings.ScenesLoadStep("Ratsversammlung"), session.Timeline.UndoLabel);

        await session.LoadSavedLayoutAsync("Taverne", [new LayoutScene(Screen, scene)], LayoutLoad.ReplaceAll);

        Assert.Equal(Strings.LayoutsLoadStep("Taverne"), session.Timeline.UndoLabel);
    }

    /// <summary>At the start of the session undo and redo have nothing to take - no label, no error, no change announced.</summary>
    [Fact]
    public async Task Undo_and_redo_at_the_start_return_nothing()
    {
        using var session = Recording();
        var changes = 0;

        session.TimelineChanged += (_, _) => changes++;

        Assert.Null(await session.UndoAsync());
        Assert.Null(await session.RedoAsync());
        Assert.Equal(0, changes);
    }

    /// <summary>One picture taken off is named; several are counted - the label names what the next press hits (Part 7).</summary>
    [Fact]
    public async Task Removing_one_picture_names_it_and_several_are_counted()
    {
        using var session = Recording();

        var orc = await session.AddItemAsync(Screen, Picture("Ork", 'a'), position: null, Cancellation);
        var goblin = await session.AddItemAsync(Screen, Picture("Goblin", 'c'), position: null, Cancellation);
        var troll = await session.AddItemAsync(Screen, Picture("Troll", 'd'), position: null, Cancellation);

        await session.RemoveItemAsync(Screen, orc, Cancellation);

        Assert.Equal(Strings.RecordingRemoveNamed("Ork"), session.Timeline.UndoLabel);

        await session.RemoveItemsAsync(Screen, [goblin, troll], Cancellation);

        Assert.Equal(Strings.RecordingRemovePictures(2), session.Timeline.UndoLabel);
    }

    /// <summary>Several pictures laid in one command are one step, counted (Part 4: one step however many).</summary>
    [Fact]
    public async Task Several_pictures_added_at_once_are_one_step()
    {
        using var session = Recording();

        await session.AddItemsAsync(Screen, [Picture("Ork", 'a'), Picture("Goblin", 'c')], Cancellation);

        Assert.Equal(Strings.RecordingAddPictures(2), session.Timeline.UndoLabel);
        Assert.Equal(1, session.Timeline.Depth);

        await session.UndoAsync();

        Assert.Empty((await session.GetSceneAsync(Screen, Cancellation)).Items);
    }

    /// <summary>
    /// A command that changes nothing a step is made of records nothing: clearing an empty screen,
    /// taking off a picture that is not there. An undo would otherwise take back nothing visible.
    /// </summary>
    [Fact]
    public async Task A_command_that_changed_nothing_is_no_step()
    {
        using var session = Recording();

        await session.ClearItemsAsync(Screen, Cancellation);
        await session.RemoveItemAsync(Screen, new ItemId(Guid.NewGuid()), Cancellation);

        Assert.Null(session.Timeline.UndoLabel);
        Assert.Equal(0, session.Timeline.Depth);
    }

    /// <summary>Changing a text's words is like renaming a picture - not a step (Part 7).</summary>
    [Fact]
    public async Task Changing_a_texts_words_is_not_a_step()
    {
        using var session = Recording();

        var text = await session.AddTextAsync(Screen, "Townsvil", 4, Cancellation);

        await session.SetTextAsync(Screen, text, "Der Kerker", 4, Cancellation);

        Assert.Equal(1, session.Timeline.Depth);
        Assert.Equal(Strings.RecordingAddText("Townsvil"), session.Timeline.UndoLabel);
    }

    /// <summary>A background set is a step and names the screen by its label.</summary>
    [Fact]
    public async Task A_background_set_is_a_step_named_after_the_screen()
    {
        using var session = Recording();

        await session.SetBackgroundAsync(Screen, Picture("Karte"), Cancellation);

        Assert.Equal(Strings.RecordingSetBackground("Karte", "TISCH-PC//1"), session.Timeline.UndoLabel);

        await session.UndoAsync();

        Assert.Null((await session.GetSceneAsync(Screen, Cancellation)).Background);
    }

    /// <summary>
    /// Behind a campaign switch lie pictures the hub can no longer deliver: every screen is cleared
    /// and the timeline emptied, so no undo brings placeholders back (Part 4).
    /// </summary>
    [Fact]
    public async Task Clearing_everywhere_empties_the_timeline()
    {
        using var session = Recording();
        var changes = 0;

        await session.AddItemAsync(Screen, Picture("Grimmbart"), position: null, Cancellation);
        await session.UndoAsync();

        session.TimelineChanged += (_, _) => changes++;
        await session.ClearEverywhereAsync(Cancellation);

        Assert.Null(session.Timeline.UndoLabel);
        Assert.Null(session.Timeline.RedoLabel);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A picture deleted from the stock cannot come back by undo (Part 4): the timeline is cut up to and
    /// including the newest step that shows it - a newer step that does not show it stays - and the
    /// picture leaves the screen.
    /// </summary>
    [Fact]
    public async Task Deleting_a_picture_from_the_stock_cuts_the_steps_that_show_it()
    {
        using var session = Recording();
        var orc = Picture("Ork", 'a');

        await session.AddItemAsync(Screen, Picture("Troll", 'd'), position: null, Cancellation);
        await session.AddItemAsync(Screen, orc, position: null, Cancellation);
        await session.AddItemAsync(Screen, Picture("Goblin", 'c'), position: null, Cancellation);
        await session.RemoveAssetsAsync([orc.AssetId], Cancellation);

        Assert.Equal(1, session.Timeline.Depth);
        Assert.Equal(Strings.RecordingAddNamed("Goblin"), session.Timeline.UndoLabel);
        Assert.DoesNotContain((await session.GetSceneAsync(Screen, Cancellation)).Items.OfType<ImageItem>(), item => item.Name == "Ork");
    }

    private static AssetRef Picture(string name, char seed) =>
        new(new AssetId(new string(seed, 64)), new AssetMeta(800, 600, "png", 1024, IsAnimated: false, new string('b', 64)), name);

    private static AssetRef Picture(string name) =>
        new(new AssetId(new string('a', 64)), new AssetMeta(800, 600, "png", 1024, IsAnimated: false, new string('b', 64)), name);

    /// <summary>A session whose screen is known - reported once, as a connecting device would.</summary>
    private static RecordingSession Recording()
    {
        var screens = new ScreenCatalog();

        screens.Report(Screen.Device, [new ScreenInfo(Screen.Screen, "TISCH-PC//1", null, new PixelSize(1920, 1080), 96, true)], reported: null);

        return new(new SessionApi(
            new SceneStore(),
            screens,
            new DisplayConnections(),
            new PairingDirectory(Options.Create(new HubOptions()), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance));
    }
}
