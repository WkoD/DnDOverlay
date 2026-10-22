using DnDOverlay.Core;

namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// Scenes and layouts on disk (Part 3): a scene in an envelope, names that are file names, the
/// overwrite question left to the control, and everything compared against the loaded list rather
/// than the file system.
/// </summary>
public sealed class SceneShelfTests : IDisposable
{
    private static readonly PixelSize Origin = new(1920, 1080);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "dndoverlay-shelf-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _time = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void A_saved_scene_reads_back_after_the_campaign_is_opened_again()
    {
        var scene = Scene("Grimmbart", "Ratsherr");

        Assert.Equal(ShelfOutcome.Done, Shelf().SaveScene("Dorfkarte", Origin, scene, overwrite: false));

        var again = Shelf();
        var saved = again.Scene("Dorfkarte");

        Assert.NotNull(saved);
        Assert.Equal(scene, saved.Scene);
        Assert.Equal(Origin, saved.Origin);
        Assert.Equal(_time.GetLocalNow(), saved.SavedAt);
        Assert.Empty(again.Unreadable);
    }

    /// <summary>A free text is saved with its scene and reads back as it was (M5b9).</summary>
    [Fact]
    public void A_free_text_is_saved_with_its_scene()
    {
        var text = new TextItem(new ItemId(Guid.NewGuid()), 0.5, 0.1, 0.08, 6.5, 0, 3, Locked: true, Parked: false, Revision: 4, Text: "Kerker von Townsvil");
        var scene = Scene("Grimmbart") with { Items = [.. Scene("Grimmbart").Items, text] };

        Assert.Equal(ShelfOutcome.Done, Shelf().SaveScene("Kerker", Origin, scene, overwrite: false));

        var saved = Shelf().Scene("Kerker");

        Assert.NotNull(saved);
        Assert.Equal(text, saved.Scene.Items.OfType<TextItem>().Single());
    }

    /// <summary>The folders come with the first save, never by themselves.</summary>
    [Fact]
    public void Opening_creates_nothing()
    {
        _ = Shelf();

        Assert.False(Directory.Exists(Path.Combine(_directory, "scenes")));
        Assert.False(Directory.Exists(Path.Combine(_directory, "layouts")));
    }

    /// <summary>A focus is a moment, a scene an arrangement: it is not saved.</summary>
    [Fact]
    public void A_focus_is_not_saved()
    {
        var scene = Scene("Grimmbart");
        var focused = scene with { FocusItems = [scene.Items[0].ItemId] };
        var shelf = Shelf();

        shelf.SaveScene("Fokus", Origin, focused, overwrite: false);

        Assert.Empty(Shelf().Scene("Fokus")!.Scene.FocusItems);
    }

    /// <summary>The question is the control's: this layer refuses rather than replaces, and a name differing only in case is the same name.</summary>
    [Fact]
    public void A_taken_name_is_refused_unless_overwriting_and_case_does_not_tell_names_apart()
    {
        var shelf = Shelf();

        shelf.SaveScene("Dorfkarte", Origin, Scene("Alt"), overwrite: false);

        Assert.Equal(ShelfOutcome.Taken, shelf.SaveScene("dorfkarte", Origin, Scene("Neu"), overwrite: false));

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(ShelfOutcome.Done, shelf.SaveScene("dorfkarte", Origin, Scene("Neu"), overwrite: true));

        var again = Shelf();
        var summary = Assert.Single(again.Scenes);

        // The new spelling, the new content, the new time - and one file, not two.
        Assert.Equal("dorfkarte", summary.Name);
        Assert.Equal("Neu", Assert.IsType<ImageItem>(Assert.Single(again.Scene("Dorfkarte")!.Scene.Items)).Name);
        Assert.Equal(_time.GetLocalNow(), summary.SavedAt);
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, "scenes")));
    }

    [Fact]
    public void A_name_that_cannot_be_a_file_is_refused_not_rewritten()
    {
        Assert.Equal(ShelfOutcome.BadName, Shelf().SaveScene("Rat 3/4", Origin, Scene("X"), overwrite: false));
        Assert.False(Directory.Exists(Path.Combine(_directory, "scenes")));
    }

    [Fact]
    public void Renaming_moves_the_file_and_a_change_of_case_is_no_collision()
    {
        var shelf = Shelf();

        shelf.SaveScene("Dorfkarte", Origin, Scene("X"), overwrite: false);
        shelf.SaveScene("Kerker", Origin, Scene("Y"), overwrite: false);

        Assert.Equal(ShelfOutcome.Taken, shelf.RenameScene("Dorfkarte", "KERKER"));
        Assert.Equal(ShelfOutcome.Done, shelf.RenameScene("Dorfkarte", "DORFKARTE"));
        Assert.Equal(ShelfOutcome.Done, shelf.RenameScene("Kerker", "Gewölbe"));
        Assert.Equal(ShelfOutcome.Unknown, shelf.RenameScene("Gibt es nicht", "X"));

        Assert.Equal(["DORFKARTE", "Gewölbe"], Shelf().Scenes.Select(scene => scene.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Deleting_takes_the_file()
    {
        var shelf = Shelf();

        shelf.SaveScene("Dorfkarte", Origin, Scene("X"), overwrite: false);

        Assert.True(shelf.DeleteScene("DORFKARTE"));
        Assert.Empty(Shelf().Scenes);
        Assert.False(shelf.DeleteScene("Dorfkarte"));
    }

    /// <summary>Newest first, by the written time - copying the folder resets file times for all entries at once.</summary>
    [Fact]
    public void Scenes_list_newest_first_by_the_written_time()
    {
        var shelf = Shelf();

        shelf.SaveScene("Erste", Origin, Scene("X"), overwrite: false);
        _time.Advance(TimeSpan.FromHours(1));
        shelf.SaveScene("Zweite", Origin, Scene("Y"), overwrite: false);

        foreach (var file in Directory.GetFiles(Path.Combine(_directory, "scenes")))
        {
            File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        Assert.Equal(["Zweite", "Erste"], Shelf().Scenes.Select(scene => scene.Name));
    }

    [Fact]
    public void A_layout_keeps_its_parts_as_copies_with_screen_names()
    {
        var screen = new ScreenRef(new DeviceId(Guid.NewGuid()), new ScreenId("TISCH//1"));
        var part = new LayoutPart(screen, "TISCH-PC//DISPLAY1", new SavedScene(Origin, _time.GetLocalNow(), Scene("Karte")));

        Assert.Equal(ShelfOutcome.Done, Shelf().SaveLayout("Abend 12", [part], overwrite: false));

        var again = Shelf();
        var layout = again.Layout("abend 12");

        Assert.NotNull(layout);
        Assert.Equal([part], layout.Parts);
        Assert.Equal(["TISCH-PC//DISPLAY1"], Assert.Single(again.Layouts).Screens);
    }

    /// <summary>A file that does not parse is reported and left alone - a scene is work, and a sync may still be running.</summary>
    [Fact]
    public void An_unreadable_file_is_reported_and_left_where_it_is()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "scenes"));
        var broken = Path.Combine(_directory, "scenes", "Kaputt.json");
        File.WriteAllText(broken, "{ not json");

        var shelf = Shelf();

        Assert.Equal([broken], shelf.Unreadable);
        Assert.True(File.Exists(broken));
    }

    /// <summary>The one hard no: a file from a newer version is not read half-way (Part 3).</summary>
    [Fact]
    public void A_file_from_a_newer_version_refuses_to_open()
    {
        Shelf().SaveScene("Dorfkarte", Origin, Scene("X"), overwrite: false);

        var path = Path.Combine(_directory, "scenes", "Dorfkarte.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal));

        Assert.Throws<CampaignSchemaException>(Shelf);
    }

    /// <summary>
    /// Names come from the campaign as it is now, never from the file - and a picture the campaign
    /// does not have keeps its saved name and is listed once, for the one summary message.
    /// </summary>
    [Fact]
    public void Loading_resolves_current_names_and_lists_what_is_missing()
    {
        var known = new AssetId(new string('a', 64));
        var gone = new AssetId(new string('b', 64));
        var scene = SceneState.Empty with
        {
            Items = [Item(known, "Altname"), Item(gone, "Verschollen"), Item(gone, "Verschollen")],
        };

        var (resolved, missing) = SceneNames.Resolve(scene, asset => asset == known ? "Neuer Name" : null);

        Assert.Equal(["Neuer Name", "Verschollen", "Verschollen"], resolved.Items.OfType<ImageItem>().Select(item => item.Name));
        Assert.Equal([gone], missing);
    }

    /// <summary>The reference display (Part 7): which scenes and layouts show a picture, as an item or as the background.</summary>
    [Fact]
    public void Usage_names_the_scenes_and_layouts_that_show_a_picture()
    {
        var shelf = Shelf();
        var orc = new AssetId(new string('a', 64));
        var map = new AssetId(new string('b', 64));

        shelf.SaveScene("Dorf", Origin, SceneState.Empty with { Items = [Item(orc, "Ork")] }, overwrite: false);
        shelf.SaveScene("Karte", Origin, SceneState.Empty with { Background = Background(map, "Karte") }, overwrite: false);
        shelf.SaveLayout("Abend", [Part(SceneState.Empty with { Items = [Item(orc, "Ork")], Background = Background(map, "Karte") })], overwrite: false);

        var usage = shelf.UsageOf(orc);

        Assert.Equal(["Dorf"], usage.Scenes);
        Assert.Equal(["Abend"], usage.Layouts);
        Assert.Equal(["Karte"], shelf.UsageOf(map).Scenes);
        Assert.False(shelf.UsageOf(new AssetId(new string('c', 64))).Used);
        Assert.Equal(2, shelf.UseCounts()[orc]);
    }

    /// <summary>A rename reaches the files, keeps their time of saving, and is there after reopening.</summary>
    [Fact]
    public void A_renamed_picture_carries_its_new_name_in_the_files()
    {
        var shelf = Shelf();
        var orc = new AssetId(new string('a', 64));

        shelf.SaveScene("Dorf", Origin, SceneState.Empty with { Items = [Item(orc, "Ork")], Background = Background(orc, "Ork") }, overwrite: false);
        shelf.SaveLayout("Abend", [Part(SceneState.Empty with { Items = [Item(orc, "Ork")] })], overwrite: false);

        var savedAt = shelf.Scene("Dorf")!.SavedAt;

        _time.Now = _time.Now.AddHours(1);
        shelf.RenameAsset(orc, "Grimmbart");

        var reopened = Shelf();
        var scene = reopened.Scene("Dorf")!;

        Assert.Equal("Grimmbart", Assert.IsType<ImageItem>(Assert.Single(scene.Scene.Items)).Name);
        Assert.Equal("Grimmbart", scene.Scene.Background?.Name);
        Assert.Equal(savedAt, scene.SavedAt);
        Assert.Equal("Grimmbart", Assert.IsType<ImageItem>(Assert.Single(reopened.Layout("Abend")!.Parts[0].Scene.Scene.Items)).Name);
    }

    /// <summary>
    /// The delete cascade (Part 7): items and backgrounds go from every file, a layout loses the screen
    /// that held nothing else, and what became empty is reported - not deleted.
    /// </summary>
    [Fact]
    public void A_delete_takes_the_pictures_out_and_reports_what_became_empty()
    {
        var shelf = Shelf();
        var orc = new AssetId(new string('a', 64));
        var goblin = new AssetId(new string('b', 64));

        shelf.SaveScene("Nur Ork", Origin, SceneState.Empty with { Items = [Item(orc, "Ork")], Background = Background(orc, "Ork") }, overwrite: false);
        shelf.SaveScene("Gemischt", Origin, SceneState.Empty with { Items = [Item(orc, "Ork"), Item(goblin, "Goblin")] }, overwrite: false);
        shelf.SaveLayout(
            "Abend",
            [Part(SceneState.Empty with { Items = [Item(orc, "Ork")] }), Part(SceneState.Empty with { Items = [Item(goblin, "Goblin")] })],
            overwrite: false);
        shelf.SaveLayout("Nur Ork", [Part(SceneState.Empty with { Items = [Item(orc, "Ork")] })], overwrite: false);

        var emptied = shelf.RemoveAssets([orc]);

        Assert.Equal(["Nur Ork"], emptied.Scenes);
        Assert.Equal(["Nur Ork"], emptied.Layouts);

        var reopened = Shelf();

        Assert.Empty(reopened.Scene("Nur Ork")!.Scene.Items);
        Assert.Null(reopened.Scene("Nur Ork")!.Scene.Background);
        Assert.Equal(goblin, Assert.IsType<ImageItem>(Assert.Single(reopened.Scene("Gemischt")!.Scene.Items)).AssetId);
        Assert.Single(reopened.Layout("Abend")!.Parts);
        Assert.False(reopened.UsageOf(orc).Used);
    }

    private SceneShelf Shelf() => SceneShelf.Open(_directory, _time);

    private static LayoutPart Part(SceneState scene) =>
        new(
            new ScreenRef(new DeviceId(Guid.NewGuid()), new ScreenId(@"\\?\DISPLAY#" + Guid.NewGuid().ToString("N"))),
            "TISCH-PC//1",
            new SavedScene(Origin, DateTimeOffset.UnixEpoch, scene));

    private static BackgroundItem Background(AssetId asset, string name) =>
        new(asset, new AssetMeta(1920, 1080, "png", 2048, false, new string('e', 64)), name, false, 0.5, 0.5, 1, 0, false);

    private static SceneState Scene(params string[] names) =>
        SceneState.Empty with { Items = [.. names.Select(name => Item(new AssetId(new string('c', 64)), name))] };

    private static ImageItem Item(AssetId asset, string name) =>
        new(
            new ItemId(Guid.NewGuid()), 0.5, 0.5, 0.4, 4d / 3d, 0, 1, false, false, 1,
            asset,
            new AssetMeta(800, 600, "png", 1024, false, new string('d', 64)),
            name,
            false,
            false);
}
