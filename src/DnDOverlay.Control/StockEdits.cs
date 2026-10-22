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

        while (NameDialog.Ask(owner, Strings.StockRenameTitle, Strings.StockRenamePrompt, suggestion, check: false) is { } typed)
        {
            // The inventory is written first; a disk that refuses it ends the rename before anything
            // else changed, with the reason in the status line rather than a crash out of a menu.
            if (!CampaignDesk.Writes(() => open.Store.Rename(asset, typed), out var outcome, out var refused))
            {
                _say(Strings.StockRenameFailed(old, refused));
                return;
            }

            switch (outcome)
            {
                case NameOutcome.Done:
                    var name = typed.Trim();

                    await _session.RenameAssetAsync(asset, name).ConfigureAwait(true);

                    // The scenes and layouts last: one that cannot be written keeps the old name in its
                    // file - loading resolves names afresh anyway (Part 3) - and the DM is told which.
                    var said = !CampaignDesk.Writes(() => open.Shelf.RenameAsset(asset, name), out var unwritten, out var stopped)
                        ? Strings.StockRenamePartly(old, name, stopped)
                        : unwritten.Count > 0
                            ? Strings.StockRenamePartly(old, name, Listed(unwritten))
                            : Strings.StockRenamed(old, name);

                    ControlLog.AssetRenamed(_logger, old, name);
                    _say(said);
                    Changed?.Invoke(this, EventArgs.Empty);
                    _desk.StockEdited();

                    return;

                case NameOutcome.Taken:
                    MessageBox.Show(owner, Strings.StockRenameTaken(typed.Trim()), Strings.StockRenameTitle);
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
            one ? Strings.StockDeleteOneQuestion(entries[0].Name) : Strings.StockDeleteManyQuestion(entries.Count),
        };

        if (lying > 0)
        {
            lines.Add(one ? Strings.StockDeleteTakenOffOne(lying) : Strings.StockDeleteTakenOffMany(lying));
        }

        if (scenes.Count > 0)
        {
            lines.Add(Strings.StockDeleteFromScenes(Listed(scenes)));
        }

        if (layouts.Count > 0)
        {
            lines.Add(Strings.StockDeleteFromLayouts(Listed(layouts)));
        }

        // Undo must never bring back a picture whose file is gone (Part 4), so the timeline is cut up to
        // the newest step that shows one - and the question says what that costs, as a number.
        var lost = _session.Timeline.CountLost(ids);

        if (lost > 0)
        {
            lines.Add(Strings.StockDeleteStepsLost(lost));
        }

        lines.Add(one ? Strings.StockDeleteFileGone : Strings.StockDeleteFilesGone);

        var answer = MessageBox.Show(
            owner,
            string.Join("\n\n", lines),
            one ? Strings.StockDeleteTitleOne : Strings.StockDeleteTitleMany,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await _session.RemoveAssetsAsync(ids).ConfigureAwait(true);

        // The screens are clear and the timeline cut; a scene file or the inventory the disk will not
        // write stops the rest. Said, and what is left stays in the stock to be deleted again - never
        // a task that ends unobserved with the DM told nothing.
        if (!CampaignDesk.Writes(() => Removed(open, ids), out var emptied, out var refused))
        {
            _say(Strings.StockDeletePartly(refused));
            Changed?.Invoke(this, EventArgs.Empty);
            _desk.StockEdited();

            return;
        }

        ControlLog.AssetsDeleted(_logger, ids.Count, lying, scenes.Count, layouts.Count);

        var said = one ? Strings.StockDeletedOne(entries[0].Name) : Strings.StockDeletedMany(entries.Count);

        // A scene or layout the disk would not rewrite still shows what was deleted - as a placeholder
        // when loaded. Named, so the DM knows where (the shelf goes on with the others).
        if (emptied.NotWritten.Count > 0)
        {
            said += " " + Strings.StockDeleteNotWritten(Listed(emptied.NotWritten));
        }

        if (emptied.Scenes.Count + emptied.Layouts.Count > 0)
        {
            said += OfferEmptied(owner, open.Shelf, emptied);
        }

        _say(said);
        Changed?.Invoke(this, EventArgs.Empty);
        _desk.StockEdited();
    }

    /// <summary>The saved files first, the stock's own files last: a reference never outlives the bytes it points at.</summary>
    private static EmptiedByDelete Removed(OpenCampaign open, IReadOnlyCollection<AssetId> ids)
    {
        var emptied = open.Shelf.RemoveAssets(ids);

        foreach (var id in ids)
        {
            open.Store.Remove(id);
        }

        return emptied;
    }

    /// <summary>The scenes and layouts that held nothing but what was deleted - named, and offered for deletion.</summary>
    private static string OfferEmptied(Window owner, SceneShelf shelf, EmptiedByDelete emptied)
    {
        var lines = new List<string>();

        if (emptied.Scenes.Count > 0)
        {
            lines.Add(Strings.StockEmptiedScenes(Listed(emptied.Scenes)));
        }

        if (emptied.Layouts.Count > 0)
        {
            lines.Add(Strings.StockEmptiedLayouts(Listed(emptied.Layouts)));
        }

        var answer = MessageBox.Show(
            owner,
            Strings.StockEmptiedQuestion(string.Join("\n", lines)),
            Strings.StockEmptiedTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (answer != MessageBoxResult.Yes)
        {
            return " " + Strings.StockEmptiedKept(emptied.Scenes.Count + emptied.Layouts.Count);
        }

        foreach (var scene in emptied.Scenes)
        {
            if (!CampaignDesk.Removes(() => shelf.DeleteScene(scene), () => shelf.Scene(scene) is not null, out var refused))
            {
                return " " + Strings.ScenesDeleteFailed(scene, refused);
            }
        }

        foreach (var layout in emptied.Layouts)
        {
            if (!CampaignDesk.Removes(() => shelf.DeleteLayout(layout), () => shelf.Layout(layout) is not null, out var refused))
            {
                return " " + Strings.LayoutsDeleteFailed(layout, refused);
            }
        }

        return " " + Strings.StockEmptiedDeleted(emptied.Scenes.Count + emptied.Layouts.Count);
    }

    private static string Listed(IReadOnlyList<string> names) =>
        names.Count <= NamesShown
            ? string.Join(", ", names)
            : Strings.StockListedMore(string.Join(", ", names.Take(NamesShown)), names.Count - NamesShown);
}
