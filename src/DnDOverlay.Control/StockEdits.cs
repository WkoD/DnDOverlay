using System.Windows;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// Renaming and deleting pictures of the stock - one place for both, whichever way the DM gets
/// there: the entry's menu, the item menu on a tile, the Del key in the stock (Part 7). Two ways in,
/// one name, no difference in the result.
/// </summary>
internal sealed class StockEdits
{
    /// <summary>How many names a question lists before it says "and N more" - a dialog is not a report.</summary>
    private const int NamesShown = 8;

    private readonly RecordingSession _session;
    private readonly CampaignDesk _desk;
    private readonly Func<IReadOnlyList<ScreenView>> _screens;
    private readonly Action<string> _say;
    private readonly ILogger _logger;

    internal StockEdits(
        RecordingSession session, CampaignDesk desk, Func<IReadOnlyList<ScreenView>> screens, Action<string> say, ILogger logger)
    {
        _session = session;
        _desk = desk;
        _screens = screens;
        _say = say;
        _logger = logger;
    }

    /// <summary>Names, pictures, scenes or layouts changed - every tab draws again.</summary>
    internal event EventHandler? Changed;

    /// <summary>
    /// Renames a picture campaign-wide (Part 3): in the stock, on every screen, in every scene and
    /// layout. A name another picture carries is refused and the dialog comes back with what was
    /// typed - the name is the one handle on a picture, and two pictures called "Krieger" would make
    /// every display of it a riddle.
    /// </summary>
    internal async Task RenameAsync(Window owner, AssetId asset)
    {
        if (_desk.Open is not { } open || open.Store.Entries.FirstOrDefault(entry => entry.AssetId == asset.Value) is not { } entry)
        {
            return;
        }

        var old = entry.Name;
        var suggestion = old;

        while (NameDialog.Ask(owner, "Rename picture", "New name of the picture - on every screen and in every scene:", suggestion, check: false) is { } typed)
        {
            switch (open.Store.Rename(asset, typed))
            {
                case NameOutcome.Done:
                    var name = typed.Trim();

                    await _session.RenameAssetAsync(asset, name).ConfigureAwait(true);
                    open.Shelf.RenameAsset(asset, name);

                    ControlLog.AssetRenamed(_logger, old, name);
                    _say($"Renamed \"{old}\" to \"{name}\".");
                    Changed?.Invoke(this, EventArgs.Empty);

                    return;

                case NameOutcome.Taken:
                    MessageBox.Show(owner, $"A picture named \"{typed.Trim()}\" exists already.", "Rename picture");
                    suggestion = typed;
                    break;

                case NameOutcome.Empty:
                    suggestion = old;
                    break;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Deletes pictures from the campaign (Part 7) - the stock, every screen, every scene and layout -
    /// after ONE question that says what goes: the screens it is taken off, the scenes and layouts it
    /// is removed from. Scenes and layouts left empty are then offered for deletion, never kept
    /// quietly. The order is the screens first, the saved files second, the stock's files last: a
    /// reference never outlives the bytes it points at.
    /// </summary>
    internal async Task DeleteAsync(Window owner, IReadOnlyList<AssetId> assets)
    {
        if (_desk.Open is not { } open)
        {
            return;
        }

        var entries = open.Store.Entries
            .Where(entry => assets.Any(asset => asset.Value == entry.AssetId))
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        var ids = entries.Select(entry => new AssetId(entry.AssetId)).ToHashSet();
        var usages = ids.Select(open.Shelf.UsageOf).ToList();
        var scenes = usages.SelectMany(usage => usage.Scenes).Distinct(FileNames.Comparer).ToList();
        var layouts = usages.SelectMany(usage => usage.Layouts).Distinct(FileNames.Comparer).ToList();
        var lying = 0;

        foreach (var view in _screens())
        {
            var scene = await _session.GetSceneAsync(view.Screen).ConfigureAwait(true);

            if (scene.Items.OfType<ImageItem>().Any(item => ids.Contains(item.AssetId))
                || (scene.Background is { } background && ids.Contains(background.AssetId)))
            {
                lying++;
            }
        }

        var one = entries.Count == 1;
        var lines = new List<string>
        {
            one ? $"Delete \"{entries[0].Name}\" from the campaign?" : $"Delete {entries.Count} pictures from the campaign?",
        };

        if (lying > 0)
        {
            lines.Add($"{(one ? "It is" : "They are")} taken off {lying} screen(s).");
        }

        if (scenes.Count > 0)
        {
            lines.Add($"Removed from the scene(s): {Listed(scenes)}.");
        }

        if (layouts.Count > 0)
        {
            lines.Add($"Removed from the layout(s): {Listed(layouts)}.");
        }

        // Undo must never bring back a picture whose file is gone (Part 4), so the timeline is cut up to
        // the newest step that shows one - and the question says what that costs, as a number.
        var lost = _session.Timeline.CountLost(ids);

        if (lost > 0)
        {
            lines.Add($"{lost} undo step(s) are lost with it.");
        }

        lines.Add(one ? "The file is deleted. This cannot be undone." : "The files are deleted. This cannot be undone.");

        var answer = MessageBox.Show(
            owner,
            string.Join("\n\n", lines),
            one ? "Delete picture" : "Delete pictures",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await _session.RemoveAssetsAsync(ids).ConfigureAwait(true);

        var emptied = open.Shelf.RemoveAssets(ids);

        foreach (var id in ids)
        {
            open.Store.Remove(id);
        }

        ControlLog.AssetsDeleted(_logger, ids.Count, lying, scenes.Count, layouts.Count);

        var said = one ? $"Deleted \"{entries[0].Name}\"." : $"Deleted {entries.Count} pictures.";

        if (emptied.Scenes.Count + emptied.Layouts.Count > 0)
        {
            said += OfferEmptied(owner, open.Shelf, emptied);
        }

        _say(said);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The scenes and layouts that held nothing but what was deleted - named, and offered for deletion.</summary>
    private static string OfferEmptied(Window owner, SceneShelf shelf, EmptiedByDelete emptied)
    {
        var lines = new List<string>();

        if (emptied.Scenes.Count > 0)
        {
            lines.Add($"Scene(s): {Listed(emptied.Scenes)}");
        }

        if (emptied.Layouts.Count > 0)
        {
            lines.Add($"Layout(s): {Listed(emptied.Layouts)}");
        }

        var answer = MessageBox.Show(
            owner,
            $"These are empty now:\n\n{string.Join("\n", lines)}\n\nDelete them too?",
            "Empty scenes and layouts",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (answer != MessageBoxResult.Yes)
        {
            return $" {emptied.Scenes.Count + emptied.Layouts.Count} empty scene(s) or layout(s) kept.";
        }

        foreach (var scene in emptied.Scenes)
        {
            shelf.DeleteScene(scene);
        }

        foreach (var layout in emptied.Layouts)
        {
            shelf.DeleteLayout(layout);
        }

        return $" {emptied.Scenes.Count + emptied.Layouts.Count} empty scene(s) or layout(s) deleted.";
    }

    private static string Listed(IReadOnlyList<string> names) =>
        names.Count <= NamesShown
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(NamesShown))} and {names.Count - NamesShown} more";
}
