using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The tab "Scenes" (Part 7): the saved scenes of the campaign as tiles with a preview and the
/// name, newest first. A double tap lays one onto the active screen; the menu loads onto any screen,
/// replacing or adding, renames, overwrites with a screen, deletes.
/// <para>
/// <b>Simple on purpose, and it says so.</b> Part 7 puts this tab into the campaign panel, which
/// docks, retracts, searches, sorts and zooms. That panel arrives with the campaigns; until then the
/// tab stands below the stage with everything a scene needs and none of the panel's mechanics.
/// </para>
/// <para>
/// <b>Loading asks no question, even over a full screen</b> - undo brings the arrangement back, and
/// a dialog before every change of scene would be the most tiresome question in the program
/// (Part 7).
/// </para>
/// </summary>
internal sealed class ScenesPanel : DockPanel
{
    private const double TileWidth = 150;

    private readonly ISessionApi _session;
    private readonly AssetStore _store;
    private readonly SceneShelf _shelf;
    private readonly Pictures _pictures;
    private readonly Func<ScreenRef?> _active;
    private readonly Func<IReadOnlyList<ScreenView>> _screens;
    private readonly Action<string> _say;
    private readonly ILogger _logger;
    private readonly WrapPanel _tiles = new();
    private readonly TextBlock _empty = new()
    {
        Text = "No scene saved yet. Save one from a screen's menu: \"Save screen as scene ...\".",
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
    };

    internal ScenesPanel(
        ISessionApi session,
        AssetStore store,
        SceneShelf shelf,
        Pictures pictures,
        Func<ScreenRef?> active,
        Func<IReadOnlyList<ScreenView>> screens,
        Action<string> say,
        ILogger logger)
    {
        _session = session;
        _store = store;
        _shelf = shelf;
        _pictures = pictures;
        _active = active;
        _screens = screens;
        _say = say;
        _logger = logger;

        Margin = new Thickness(0, 12, 0, 0);

        var heading = new TextBlock { Text = "Scenes", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };

        SetDock(heading, Dock.Top);
        Children.Add(heading);

        var scroller = new ScrollViewer
        {
            Content = new StackPanel { Children = { _empty, _tiles } },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 230,
            PanningMode = PanningMode.VerticalOnly,
        };

        Children.Add(scroller);

        Refresh();
    }

    /// <summary>Draws the list again - after a save, a rename, a delete.</summary>
    internal void Refresh()
    {
        _tiles.Children.Clear();

        foreach (var summary in _shelf.Scenes)
        {
            _tiles.Children.Add(Tile(summary));
        }

        _empty.Visibility = _tiles.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Saves what lies on a screen as a scene - the way scenes come into being: lay out, push into
    /// place, save (Part 7). The suggestion is <c>Scene &lt;screen&gt; &lt;time&gt;</c>, cleaned to a
    /// file name; a taken name asks before it overwrites, like a file dialog.
    /// </summary>
    internal async Task SaveAsync(Window owner, ScreenRef screen, string? overwriting = null)
    {
        var view = _screens().FirstOrDefault(known => known.Screen == screen);
        var label = view?.Info.Label ?? screen.Screen.Value;

        var name = overwriting ?? NameDialog.Ask(
            owner,
            "Save screen as scene",
            $"Name of the scene from {label}:",
            FileNames.Suggest($"Scene {label} {FileNames.Stamp(DateTimeOffset.Now)}"));

        if (name is null)
        {
            return;
        }

        var scene = await _session.GetSceneAsync(screen, CancellationToken.None).ConfigureAwait(true);
        var origin = view?.Info.Size ?? new PixelSize(1920, 1080);

        var outcome = _shelf.SaveScene(name, origin, scene, overwrite: overwriting is not null);

        if (outcome is ShelfOutcome.Taken)
        {
            var answer = MessageBox.Show(
                owner,
                $"A scene named \"{name}\" exists already. Overwrite it?",
                "Save screen as scene",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            outcome = _shelf.SaveScene(name, origin, scene, overwrite: true);
        }

        if (outcome is ShelfOutcome.Done)
        {
            ControlLog.SceneSaved(_logger, name, scene.Items.Count);
            _say($"Saved \"{name}\".");
            Refresh();
        }
    }

    private StackPanel Tile(SceneSummary summary)
    {
        var preview = new SceneThumbnail(_pictures)
        {
            Width = TileWidth,
            Height = TileWidth / Math.Max(0.1, summary.Origin.AspectRatio),
        };

        if (_shelf.Scene(summary.Name) is { } saved)
        {
            preview.Show(saved.Scene, ScreenContext.Default(saved.Origin, 96), default);
            preview.InvalidateVisual();
        }

        var name = new TextBlock
        {
            Text = summary.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = TileWidth,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var tile = new StackPanel
        {
            Margin = new Thickness(0, 0, 8, 8),
            Background = Brushes.Transparent,
            ToolTip = string.Create(
                CultureInfo.CurrentCulture,
                $"{summary.Name} - {summary.Origin.Width}×{summary.Origin.Height}, saved {summary.SavedAt.LocalDateTime:g}"),
        };

        tile.Children.Add(new Border { Child = preview, BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(1) });
        tile.Children.Add(name);

        // A double tap and a double click alike: the touch is promoted to a click, and a click count
        // of two is what both arrive as.
        tile.MouseLeftButtonDown += async (_, down) =>
        {
            if (down.ClickCount == 2)
            {
                down.Handled = true;

                if (_active() is { } screen)
                {
                    await LoadAsync(summary.Name, screen, SceneLoad.Replace).ConfigureAwait(true);
                }
                else
                {
                    _say("No screen to load onto.");
                }
            }
        };

        tile.ContextMenu = Menu(summary.Name);
        tile.ContextMenuOpening += (_, _) => tile.ContextMenu = Menu(summary.Name);

        return tile;
    }

    private ContextMenu Menu(string name)
    {
        var menu = new ContextMenu();
        var load = new MenuItem { Header = "Load on screen" };
        var replace = new MenuItem { Header = "Replace" };
        var add = new MenuItem { Header = "Add" };
        var overwrite = new MenuItem { Header = "Overwrite with screen" };

        foreach (var view in _screens())
        {
            var screen = view.Screen;

            replace.Items.Add(Entry(view.Info.Label, () => _ = LoadAsync(name, screen, SceneLoad.Replace)));
            add.Items.Add(Entry(view.Info.Label, () => _ = LoadAsync(name, screen, SceneLoad.Add)));
            overwrite.Items.Add(Entry(view.Info.Label, () => _ = SaveAsync(Window.GetWindow(this)!, screen, overwriting: name)));
        }

        load.Items.Add(replace);
        load.Items.Add(add);

        menu.Items.Add(load);
        menu.Items.Add(Entry("Rename ...", () => Rename(name)));
        menu.Items.Add(overwrite);
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry("Delete", () => Delete(name)));

        return menu;
    }

    /// <summary>
    /// Loads a scene: the names resolved afresh from the campaign, the pictures it does not have
    /// counted for ONE message (Part 3), and the arrangement computed against the target by the
    /// hub (siting question 4).
    /// </summary>
    private async Task LoadAsync(string name, ScreenRef screen, SceneLoad mode)
    {
        if (_shelf.Scene(name) is not { } saved)
        {
            return;
        }

        var names = _store.Entries.ToDictionary(entry => entry.AssetId, entry => entry.Name, StringComparer.Ordinal);
        var (scene, missing) = SceneNames.Resolve(saved.Scene, asset => names.GetValueOrDefault(asset.Value));

        await _session.LoadSceneAsync(screen, scene, mode, CancellationToken.None).ConfigureAwait(true);

        var pictures = scene.Items.OfType<ImageItem>().Select(item => item.AssetId)
            .Concat(scene.Background is { } background ? [background.AssetId] : [])
            .Distinct()
            .Count();

        var how = mode.ToString();

        ControlLog.SceneLoaded(_logger, name, how, scene.Items.Count, missing.Count);

        _say(missing.Count == 0
            ? $"Loaded \"{name}\"."
            : $"Loaded \"{name}\" - {missing.Count} of {pictures} pictures are missing and stand as placeholders.");
    }

    private void Rename(string name)
    {
        if (NameDialog.Ask(Window.GetWindow(this)!, "Rename scene", "New name of the scene:", name) is not { } typed)
        {
            return;
        }

        switch (_shelf.RenameScene(name, typed))
        {
            case ShelfOutcome.Done:
                Refresh();
                break;

            case ShelfOutcome.Taken:
                _say($"A scene named \"{typed}\" exists already.");
                break;

            default:
                break;
        }
    }

    private void Delete(string name)
    {
        var answer = MessageBox.Show(
            Window.GetWindow(this)!,
            $"Delete the scene \"{name}\"? The pictures stay in the stock.",
            "Delete scene",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes && _shelf.DeleteScene(name))
        {
            Refresh();
        }
    }

    private static MenuItem Entry(string header, Action chosen)
    {
        var item = new MenuItem { Header = header };

        item.Click += (_, _) => chosen();

        return item;
    }
}
