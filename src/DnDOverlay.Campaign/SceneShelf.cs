using System.Text.Json;
using System.Text.Json.Serialization;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign;

/// <summary>
/// A saved scene: a <see cref="SceneState"/> in an envelope (Part 3). The envelope carries what
/// belongs to the FILE and would make no sense in the reducer.
/// </summary>
/// <param name="Origin">
/// The size of the screen it came from - the measure, not the ratio, because a DM recognises
/// <c>1920×1080</c> faster than <c>16:9</c>. Used for the preview only; loading computes against
/// the target screen.
/// </param>
/// <param name="SavedAt">
/// Written, never read off the file's time stamp: copying the folder - the exchange format -
/// resets those, for every entry at once. Overwriting renews it, so "newest first" puts last
/// night's work at the top.
/// </param>
public sealed record SavedScene(PixelSize Origin, DateTimeOffset SavedAt, SceneState Scene);

/// <summary>One screen of a layout: where it goes, by address and by name, and the scene - a copy, not a reference (Part 3).</summary>
/// <param name="ScreenName">
/// The second way of finding the screen: a <see cref="ScreenRef"/> dies with <c>display.json</c>,
/// the name lives in the control and survives a reinstalled device.
/// </param>
public sealed record LayoutPart(ScreenRef Screen, string ScreenName, SavedScene Scene);

/// <summary>A saved layout: several screens at one grip, not necessarily all of them (Part 3).</summary>
public sealed record SavedLayout(DateTimeOffset SavedAt, IReadOnlyList<LayoutPart> Parts)
{
    /// <summary>Structural over the parts - see <see cref="SceneState"/>.</summary>
    public bool Equals(SavedLayout? other) =>
        other is not null && SavedAt == other.SavedAt && Parts.SequenceEqual(other.Parts);

    public override int GetHashCode() => HashCode.Combine(SavedAt, Parts.Count);
}

/// <summary>A scene as the tab lists it - name, time, origin - without its content.</summary>
public sealed record SceneSummary(string Name, DateTimeOffset SavedAt, PixelSize Origin);

/// <summary>A layout as the tab lists it - name, time, and the names of the screens it fills.</summary>
public sealed record LayoutSummary(string Name, DateTimeOffset SavedAt, IReadOnlyList<string> Screens);

/// <summary>Where a picture is used: the saved scenes and the layouts that show it, by name (Part 7).</summary>
public sealed record AssetUsage(IReadOnlyList<string> Scenes, IReadOnlyList<string> Layouts)
{
    /// <summary>Whether anything saved needs the picture.</summary>
    public bool Used => Scenes.Count > 0 || Layouts.Count > 0;
}

/// <summary>
/// What a delete left behind: scenes and layouts that held nothing else and are empty now - offered
/// for deletion, never kept quietly (Part 7).
/// </summary>
public sealed record EmptiedByDelete(IReadOnlyList<string> Scenes, IReadOnlyList<string> Layouts);

/// <summary>What became of a save or a rename.</summary>
public enum ShelfOutcome
{
    /// <summary>Written.</summary>
    Done,

    /// <summary>The name is taken and overwriting was not allowed - the control asks (Part 3).</summary>
    Taken,

    /// <summary>The name cannot be a file name; <see cref="FileNames.Check"/> says why.</summary>
    BadName,

    /// <summary>There is no scene or layout of that name.</summary>
    Unknown,
}

/// <summary>
/// The scenes and layouts of one campaign: <c>scenes\&lt;name&gt;.json</c> and
/// <c>layouts\&lt;name&gt;.json</c>, read completely when the campaign opens and kept in memory
/// (Part 3).
/// <para>
/// <b>Compared against the loaded list, never against the file system.</b> <c>File.Exists</c>
/// would answer per platform - on Windows it finds <c>Dorfkarte.json</c> for <c>dorfkarte</c>,
/// on Linux not - and the one new question in the whole program, "overwrite?", would hang on the
/// file system rather than on our rule. Reading everything on open is affordable because scenes
/// carry no picture data: two hundred scenes are a few hundred kilobytes. The list includes the
/// files that would not read: they are kept as work (<see cref="Unreadable"/>), so their names are
/// taken like any other - a save asks before replacing one, a rename does not land on one.
/// </para>
/// </summary>
public sealed class SceneShelf
{
    private const string ScenesFolder = "scenes";
    private const string LayoutsFolder = "layouts";
    private const string Extension = ".json";

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, SavedScene> _scenes = new(FileNames.Comparer);
    private readonly Dictionary<string, SavedLayout> _layouts = new(FileNames.Comparer);
    private readonly List<string> _unreadable = [];

    private SceneShelf(string directory, TimeProvider time)
    {
        _directory = directory;
        _time = time;
    }

    /// <summary>
    /// Files that would not read - for the report beside the stage when the campaign opens. They
    /// are left where they are: a scene is work, and a file that does not parse today may be one
    /// half-copied by a sync that is still running (Part 3).
    /// </summary>
    public IReadOnlyList<string> Unreadable => _unreadable;

    /// <summary>The scenes, newest first.</summary>
    public IReadOnlyList<SceneSummary> Scenes =>
        [.. _scenes.Select(pair => new SceneSummary(pair.Key, pair.Value.SavedAt, pair.Value.Origin))
            .OrderByDescending(summary => summary.SavedAt)];

    /// <summary>The layouts, newest first.</summary>
    public IReadOnlyList<LayoutSummary> Layouts =>
        [.. _layouts.Select(pair => new LayoutSummary(
                pair.Key, pair.Value.SavedAt, [.. pair.Value.Parts.Select(part => part.ScreenName)]))
            .OrderByDescending(summary => summary.SavedAt)];

    /// <summary>Every saved scene and every scene inside a layout - what the reference display and the delete cascade read (Part 7).</summary>
    public IEnumerable<(string Name, bool InLayout, SceneState Scene)> All =>
        _scenes.Select(pair => (pair.Key, false, pair.Value.Scene))
            .Concat(_layouts.SelectMany(pair => pair.Value.Parts.Select(part => (pair.Key, true, part.Scene.Scene))));

    /// <summary>
    /// Reads every scene and layout of the campaign at <paramref name="campaignDirectory"/>. The
    /// folders need not exist: they come into being with the first save, never by themselves.
    /// </summary>
    /// <exception cref="CampaignSchemaException">A file was written by a newer version - the one hard no (Part 3).</exception>
    public static SceneShelf Open(string campaignDirectory, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignDirectory);
        ArgumentNullException.ThrowIfNull(time);

        var shelf = new SceneShelf(campaignDirectory, time);

        foreach (var (name, path) in shelf.Files(ScenesFolder))
        {
            if (Read(path, CampaignJsonContext.Default.SceneFile) is { } file)
            {
                shelf._scenes[name] = file.ToScene();
            }
            else
            {
                shelf._unreadable.Add(path);
            }
        }

        foreach (var (name, path) in shelf.Files(LayoutsFolder))
        {
            if (Read(path, CampaignJsonContext.Default.LayoutFile) is { } file)
            {
                shelf._layouts[name] = file.ToLayout();
            }
            else
            {
                shelf._unreadable.Add(path);
            }
        }

        return shelf;
    }

    /// <summary>One saved scene by name, as it was saved - names are resolved afresh by <see cref="SceneNames"/>.</summary>
    public SavedScene? Scene(string name) => _scenes.GetValueOrDefault(name);

    /// <summary>One saved layout by name.</summary>
    public SavedLayout? Layout(string name) => _layouts.GetValueOrDefault(name);

    /// <summary>
    /// Saves a scene under a typed name. The name is checked, never rewritten; a taken name with
    /// <paramref name="overwrite"/> off answers <see cref="ShelfOutcome.Taken"/> instead of
    /// replacing, because the question belongs to the control, not to this layer (Part 3). A focus
    /// lying on the scene is not saved - a scene is an arrangement, a focus a moment.
    /// </summary>
    public ShelfOutcome SaveScene(string name, PixelSize origin, SceneState scene, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (Refused(name, Taken(ScenesFolder, _scenes), overwrite) is { } refused)
        {
            return refused;
        }

        WriteScene(name, new SavedScene(origin, _time.GetLocalNow(), scene with { FocusItems = [] }));

        return ShelfOutcome.Done;
    }

    /// <summary>Saves a layout under a typed name, with the same rules as a scene.</summary>
    public ShelfOutcome SaveLayout(string name, IReadOnlyList<LayoutPart> parts, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(parts);

        if (Refused(name, Taken(LayoutsFolder, _layouts), overwrite) is { } refused)
        {
            return refused;
        }

        WriteLayout(name, new SavedLayout(
            _time.GetLocalNow(),
            [.. parts.Select(part => part with { Scene = part.Scene with { Scene = part.Scene.Scene with { FocusItems = [] } } })]));

        return ShelfOutcome.Done;
    }

    /// <summary>Renames a scene and its file. A change of case only is a rename, not a collision.</summary>
    public ShelfOutcome RenameScene(string name, string newName) =>
        Rename(ScenesFolder, _scenes, name, newName);

    /// <summary>Renames a layout and its file.</summary>
    public ShelfOutcome RenameLayout(string name, string newName) =>
        Rename(LayoutsFolder, _layouts, name, newName);

    /// <summary>Deletes a scene and its file.</summary>
    public bool DeleteScene(string name) => Delete(ScenesFolder, _scenes, name);

    /// <summary>Deletes a layout and its file.</summary>
    public bool DeleteLayout(string name) => Delete(LayoutsFolder, _layouts, name);

    /// <summary>Which saved scenes and layouts show a picture - the reference display before a delete (Part 7).</summary>
    public AssetUsage UsageOf(AssetId asset) =>
        new(
            [.. _scenes.Where(pair => Shows(pair.Value.Scene, asset)).Select(pair => pair.Key).Order(FileNames.Comparer)],
            [.. _layouts.Where(pair => pair.Value.Parts.Any(part => Shows(part.Scene.Scene, asset))).Select(pair => pair.Key).Order(FileNames.Comparer)]);

    /// <summary>
    /// For every picture used anywhere, in how many saved scenes and layouts - one pass over the shelf
    /// for the marks of a whole grid, rather than one per tile.
    /// </summary>
    public IReadOnlyDictionary<AssetId, int> UseCounts()
    {
        var counts = new Dictionary<AssetId, int>();

        void Count(IEnumerable<SceneState> scenes)
        {
            foreach (var asset in scenes.SelectMany(Pictures).Distinct())
            {
                counts[asset] = counts.GetValueOrDefault(asset) + 1;
            }
        }

        foreach (var scene in _scenes.Values)
        {
            Count([scene.Scene]);
        }

        foreach (var layout in _layouts.Values)
        {
            Count(layout.Parts.Select(part => part.Scene.Scene));
        }

        return counts;
    }

    /// <summary>
    /// Carries a picture's new name into every scene and layout that shows it. Names are resolved
    /// afresh when a scene is loaded anyway (Part 3); written here too, so a placeholder for a picture
    /// that goes missing later and the previews of both tabs say the current name. The time of saving
    /// stays: this is not the DM saving.
    /// </summary>
    public void RenameAsset(AssetId asset, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var (key, saved) in _scenes.Where(pair => Shows(pair.Value.Scene, asset)).ToList())
        {
            WriteScene(key, saved with { Scene = Named(saved.Scene, asset, name) });
        }

        foreach (var (key, saved) in _layouts.Where(pair => pair.Value.Parts.Any(part => Shows(part.Scene.Scene, asset))).ToList())
        {
            WriteLayout(key, saved with
            {
                Parts = [.. saved.Parts.Select(part => part with { Scene = part.Scene with { Scene = Named(part.Scene.Scene, asset, name) } })],
            });
        }
    }

    /// <summary>
    /// The cascade of a delete (Part 7): the pictures' items and backgrounds are taken out of every
    /// scene and layout - removed, not left as placeholders; whoever deletes on purpose wants them
    /// gone. A layout loses the screens that held nothing else. Returns what is empty now, so the
    /// control can offer it for deletion.
    /// </summary>
    public EmptiedByDelete RemoveAssets(IReadOnlyCollection<AssetId> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var gone = assets.ToHashSet();
        var emptiedScenes = new List<string>();
        var emptiedLayouts = new List<string>();

        foreach (var (key, saved) in _scenes.Where(pair => gone.Any(asset => Shows(pair.Value.Scene, asset))).ToList())
        {
            var left = Without(saved.Scene, gone);

            WriteScene(key, saved with { Scene = left });

            if (IsEmpty(left))
            {
                emptiedScenes.Add(key);
            }
        }

        foreach (var (key, saved) in _layouts.Where(pair => pair.Value.Parts.Any(part => gone.Any(asset => Shows(part.Scene.Scene, asset)))).ToList())
        {
            var parts = saved.Parts
                .Select(part => part with { Scene = part.Scene with { Scene = Without(part.Scene.Scene, gone) } })
                .Where(part => !IsEmpty(part.Scene.Scene))
                .ToList();

            WriteLayout(key, saved with { Parts = parts });

            if (parts.Count == 0)
            {
                emptiedLayouts.Add(key);
            }
        }

        return new EmptiedByDelete(emptiedScenes, emptiedLayouts);
    }

    private void WriteScene(string name, SavedScene saved) =>
        Replace(ScenesFolder, _scenes, name, saved, JsonSerializer.SerializeToUtf8Bytes(
            SceneFile.Of(saved), CampaignJsonContext.Default.SceneFile));

    private void WriteLayout(string name, SavedLayout saved) =>
        Replace(LayoutsFolder, _layouts, name, saved, JsonSerializer.SerializeToUtf8Bytes(
            LayoutFile.Of(saved), CampaignJsonContext.Default.LayoutFile));

    private static IEnumerable<AssetId> Pictures(SceneState scene) =>
        scene.Items.OfType<ImageItem>().Select(item => item.AssetId)
            .Concat(scene.Background is { } background ? [background.AssetId] : []);

    private static bool Shows(SceneState scene, AssetId asset) => Pictures(scene).Contains(asset);

    private static bool IsEmpty(SceneState scene) => scene.Items.Count == 0 && scene.Background is null;

    private static SceneState Without(SceneState scene, HashSet<AssetId> gone) =>
        scene with
        {
            Items = [.. scene.Items.Where(item => item is not ImageItem image || !gone.Contains(image.AssetId))],
            Background = scene.Background is { } background && gone.Contains(background.AssetId) ? null : scene.Background,
        };

    private static SceneState Named(SceneState scene, AssetId asset, string name) =>
        scene with
        {
            Items = [.. scene.Items.Select(item => item is ImageItem image && image.AssetId == asset ? image with { Name = name } : item)],
            Background = scene.Background is { } background && background.AssetId == asset ? background with { Name = name } : scene.Background,
        };

    /// <summary>The names in a folder that are taken: the loaded ones and those of the files that would not read.</summary>
    private IEnumerable<string> Taken<T>(string folder, Dictionary<string, T> shelf) =>
        shelf.Keys.Concat(UnreadableNames(folder));

    private IEnumerable<string> UnreadableNames(string folder) =>
        _unreadable
            .Where(path => string.Equals(Path.GetDirectoryName(path), Path.Combine(_directory, folder), StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileNameWithoutExtension(path));

    /// <summary>
    /// The spelling under which a name is on disk in this folder - loaded or unreadable - or
    /// <see langword="null"/>. Compared by our rule, not by asking the file system (see the type).
    /// </summary>
    private string? Existing<T>(string folder, Dictionary<string, T> shelf, string name) =>
        Taken(folder, shelf).FirstOrDefault(known => FileNames.Comparer.Equals(known, name));

    private static ShelfOutcome? Refused(string name, IEnumerable<string> taken, bool overwrite)
    {
        if (FileNames.Check(name) is not null)
        {
            return ShelfOutcome.BadName;
        }

        return !overwrite && taken.Contains(name, FileNames.Comparer) ? ShelfOutcome.Taken : null;
    }

    private void Replace<T>(string folder, Dictionary<string, T> shelf, string name, T value, byte[] content)
    {
        System.IO.Directory.CreateDirectory(Path.Combine(_directory, folder));

        // An overwrite under a different case keeps the new spelling: the old file goes first, or
        // on a case-insensitive file system the write would land in it under its old name.
        if (Existing(folder, shelf, name) is { } existing
            && !string.Equals(existing, name, StringComparison.Ordinal))
        {
            File.Delete(PathOf(folder, existing));
            shelf.Remove(existing);
            _unreadable.RemoveAll(path => string.Equals(path, PathOf(folder, existing), StringComparison.OrdinalIgnoreCase));
        }

        AtomicFile.Write(PathOf(folder, name), content);
        shelf[name] = value;

        // Overwritten after the question: what would not read is replaced by what the DM saved.
        _unreadable.RemoveAll(path => string.Equals(path, PathOf(folder, name), StringComparison.OrdinalIgnoreCase));
    }

    private ShelfOutcome Rename<T>(string folder, Dictionary<string, T> shelf, string name, string newName)
    {
        if (shelf.Keys.FirstOrDefault(known => FileNames.Comparer.Equals(known, name)) is not { } existing)
        {
            return ShelfOutcome.Unknown;
        }

        if (FileNames.Check(newName) is not null)
        {
            return ShelfOutcome.BadName;
        }

        var sameEntry = FileNames.Comparer.Equals(existing, newName);

        if (!sameEntry && Existing(folder, shelf, newName) is not null)
        {
            return ShelfOutcome.Taken;
        }

        var value = shelf[existing];
        var from = PathOf(folder, existing);
        var to = PathOf(folder, newName);

        if (sameEntry)
        {
            // Only the case changes: through a temporary name, or a case-insensitive file system
            // would call it a move onto itself and keep the old spelling.
            var between = from + ".renaming";

            File.Move(from, between);
            File.Move(between, to);
        }
        else
        {
            File.Move(from, to);
        }

        shelf.Remove(existing);
        shelf[newName] = value;

        return ShelfOutcome.Done;
    }

    private bool Delete<T>(string folder, Dictionary<string, T> shelf, string name)
    {
        if (shelf.Keys.FirstOrDefault(known => FileNames.Comparer.Equals(known, name)) is not { } existing)
        {
            return false;
        }

        File.Delete(PathOf(folder, existing));
        shelf.Remove(existing);

        return true;
    }

    private string PathOf(string folder, string name) => Path.Combine(_directory, folder, name + Extension);

    private IEnumerable<(string Name, string Path)> Files(string folder)
    {
        var path = Path.Combine(_directory, folder);

        if (!System.IO.Directory.Exists(path))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(path, "*" + Extension)
            .Select(file => (System.IO.Path.GetFileNameWithoutExtension(file), file));
    }

    private static T? Read<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class, IVersioned
    {
        T? file;

        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllBytes(path), type);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        if (file is null)
        {
            return null;
        }

        if (file.SchemaVersion > InventoryDocument.CurrentSchemaVersion)
        {
            throw new CampaignSchemaException(
                Strings.SceneFileTooNew(path, file.SchemaVersion, InventoryDocument.CurrentSchemaVersion));
        }

        return file;
    }
}

/// <summary>Turns the names a scene was saved with into the names the campaign has now (Part 3).</summary>
public static class SceneNames
{
    /// <summary>
    /// The scene with every picture's CURRENT name, never the saved one - an old scene would bring
    /// old names back otherwise and undo "one picture, one name" - and the pictures the campaign
    /// does not have, for the one summary message. Those keep their saved name, so the placeholder
    /// at the table says which picture is missing.
    /// </summary>
    /// <param name="current">The campaign's name for a picture, or <see langword="null"/> when it has none.</param>
    public static (SceneState Scene, IReadOnlyList<AssetId> Missing) Resolve(SceneState scene, Func<AssetId, string?> current)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(current);

        var missing = new List<AssetId>();

        string? Named(AssetId asset, string? saved)
        {
            if (current(asset) is { } name)
            {
                return name;
            }

            if (!missing.Contains(asset))
            {
                missing.Add(asset);
            }

            return saved;
        }

        var items = scene.Items
            .Select(item => item is ImageItem image ? image with { Name = Named(image.AssetId, image.Name) ?? image.Name } : item)
            .ToList();

        var background = scene.Background is { } layer
            ? layer with { Name = Named(layer.AssetId, layer.Name) }
            : null;

        return (scene with { Items = items, Background = background }, missing);
    }
}

/// <summary>Both kinds of file carry the campaign's one schema version (Part 6, rule 6).</summary>
internal interface IVersioned
{
    int SchemaVersion { get; }
}

/// <summary>The scene file on disk.</summary>
internal sealed record SceneFile(int SchemaVersion, PixelSize Origin, DateTimeOffset SavedAt, SceneState Scene) : IVersioned
{
    internal static SceneFile Of(SavedScene saved) =>
        new(InventoryDocument.CurrentSchemaVersion, saved.Origin, saved.SavedAt, saved.Scene);

    internal SavedScene ToScene() => new(Origin, SavedAt, Scene);
}

/// <summary>The layout file on disk.</summary>
internal sealed record LayoutFile(int SchemaVersion, DateTimeOffset SavedAt, IReadOnlyList<LayoutPart> Parts) : IVersioned
{
    internal static LayoutFile Of(SavedLayout saved) =>
        new(InventoryDocument.CurrentSchemaVersion, saved.SavedAt, saved.Parts);

    internal SavedLayout ToLayout() => new(SavedAt, Parts);
}

/// <summary>
/// The files of a campaign that hold arrangements, source-generated with the protocol's options -
/// a scene on disk reads exactly like a scene on the wire, and the scene item is resolved over the
/// same fixed list of types (Part 4).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(SceneFile))]
[JsonSerializable(typeof(LayoutFile))]
internal sealed partial class CampaignJsonContext : JsonSerializerContext;
