using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The tab "Scenes" (Part 7): the saved scenes of the campaign as tiles with a preview and the
/// name, newest first. A double tap lays one onto the active screen; the menu loads onto any screen,
/// replacing or adding, renames, overwrites with a screen, deletes.
/// <para>
/// A tab of the campaign panel since M5b3: the same grid, filter, order and zoom as the pictures,
/// with the size of the screen it came from top left - the DM recognises his screens by their
/// measures - and the preview drawn in that shape (Part 7).
/// </para>
/// <para>
/// <b>Loading asks no question, even over a full screen</b> - undo brings the arrangement back, and
/// a dialog before every change of scene would be the most tiresome question in the program
/// (Part 7).
/// </para>
/// </summary>
internal sealed class ScenesPanel : Grid
{

    private readonly ISessionApi _session;
    private readonly CampaignDesk _desk;
    private readonly Pictures _pictures;
    private readonly Func<ScreenRef?> _active;
    private readonly Func<IReadOnlyList<ScreenView>> _screens;
    private readonly Action<string> _say;
    private readonly ILogger _logger;
    private readonly WrapPanel _tiles = new();
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _empty = new()
    {
        Text = "No scene saved yet. Save one from a screen's menu: \"Save screen as scene ...\".",
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        Margin = new Thickness(4),
    };

    private string _filter = string.Empty;
    private PanelSort _sort = PanelSort.Newest;
    private double _zoom = 160;

    internal ScenesPanel(
        ISessionApi session,
        CampaignDesk desk,
        Pictures pictures,
        Func<ScreenRef?> active,
        Func<IReadOnlyList<ScreenView>> screens,
        Action<string> say,
        ILogger logger)
    {
        _session = session;
        _desk = desk;
        _pictures = pictures;
        _active = active;
        _screens = screens;
        _say = say;
        _logger = logger;

        _scroller = new ScrollViewer
        {
            Content = new StackPanel { Children = { _empty, _tiles } },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.Both,
        };

        Children.Add(_scroller);

        Refresh();
    }

    /// <summary>Draws the grid for this filter, order, tile size and lie of the panel.</summary>
    internal void Apply(string filter, PanelSort sort, double zoom, bool lying)
    {
        _filter = filter;
        _sort = sort;
        _zoom = zoom;

        _tiles.Orientation = lying ? Orientation.Vertical : Orientation.Horizontal;
        _scroller.VerticalScrollBarVisibility = lying ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _scroller.HorizontalScrollBarVisibility = lying ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        Refresh();
    }

    /// <summary>Draws the list again - after a save, a rename, a delete.</summary>
    internal void Refresh()
    {
        _tiles.Children.Clear();

        if (_desk.Open is not { } open)
        {
            _empty.Text = "No campaign open.";
            _empty.Visibility = Visibility.Visible;
            return;
        }

        var scenes = open.Shelf.Scenes.Where(summary => NameFilter.Matches(NameFilter.Normalise(summary.Name), _filter));

        scenes = _sort switch
        {
            PanelSort.Oldest => scenes.OrderBy(summary => summary.SavedAt),
            PanelSort.NameAscending => scenes.OrderBy(summary => summary.Name, StringComparer.CurrentCultureIgnoreCase),
            PanelSort.NameDescending => scenes.OrderByDescending(summary => summary.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => scenes.OrderByDescending(summary => summary.SavedAt),
        };

        foreach (var summary in scenes)
        {
            _tiles.Children.Add(Tile(open.Shelf, summary));
        }

        _empty.Text = open.Shelf.Scenes.Count == 0
            ? "No scene saved yet. Save one from a screen's menu: \"Save screen as scene ...\"."
            : "Nothing matches the filter.";
        _empty.Visibility = _tiles.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Saves what lies on a screen as a scene - the way scenes come into being: lay out, push into
    /// place, save (Part 7). The suggestion is <c>Scene &lt;screen&gt; &lt;time&gt;</c>, cleaned to a
    /// file name; a taken name asks before it overwrites, like a file dialog.
    /// </summary>
    internal async Task SaveAsync(Window owner, ScreenRef screen, string? overwriting = null)
    {
        if (_desk.Open is null)
        {
            _say("No campaign is open - open one, or add pictures to begin one. A scene lives in its campaign.");
            return;
        }

        // <b>Saving a scene names the campaign</b> (Part 3): an unnamed one is asked for its name
        // once, its folder renamed, and then the scene saved. No scene without a home.
        if (_desk.Open.Unnamed && !NameCampaign(owner))
        {
            return;
        }

        if (_desk.Open is not { } open)
        {
            return;
        }

        var shelf = open.Shelf;
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

        var outcome = shelf.SaveScene(name, origin, scene, overwrite: overwriting is not null);

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

            outcome = shelf.SaveScene(name, origin, scene, overwrite: true);
        }

        if (outcome is ShelfOutcome.Done)
        {
            ControlLog.SceneSaved(_logger, name, scene.Items.Count);
            _say($"Saved \"{name}\".");
            Refresh();
        }
    }

    /// <summary>Asks the unnamed campaign for its first name; false when the DM cancelled.</summary>
    private bool NameCampaign(Window owner)
    {
        while (NameDialog.Ask(owner, "Name the campaign", "This campaign has no name yet. A scene needs a named campaign - what is it called?", string.Empty) is { } typed)
        {
            switch (_desk.Rename(typed))
            {
                case CampaignOutcome.Done:
                    return true;

                case CampaignOutcome.Taken:
                    MessageBox.Show(owner, $"A campaign named \"{typed}\" exists already.", "Name the campaign");
                    break;

                case CampaignOutcome.Busy:
                    MessageBox.Show(owner, "The folder is in use right now - try again in a moment.", "Name the campaign");
                    break;

                default:
                    break;
            }
        }

        return false;
    }

    private Grid Tile(SceneShelf shelf, SceneSummary summary)
    {
        // The frame in the saved measures with room around it in the tile: a scene saved upright
        // LOOKS upright, even at the smallest zoom where the overlays are gone (Part 7).
        var aspect = Math.Max(0.1, summary.Origin.AspectRatio);
        var room = _zoom * 0.9;
        var preview = new SceneThumbnail(_pictures)
        {
            Width = aspect >= 1 ? room : room * aspect,
            Height = aspect >= 1 ? room / aspect : room,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (shelf.Scene(summary.Name) is { } saved)
        {
            preview.Show(saved.Scene, ScreenContext.Default(saved.Origin, 96), default);
            preview.InvalidateVisual();
        }

        var tile = new Grid
        {
            Width = _zoom,
            Height = _zoom,
            Margin = new Thickness(0, 0, 6, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            ClipToBounds = true,
            ToolTip = string.Create(
                CultureInfo.CurrentCulture,
                $"{summary.Name} - {summary.Origin.Width}×{summary.Origin.Height}, saved {summary.SavedAt.LocalDateTime:g}"),
        };

        tile.Children.Add(preview);

        if (_zoom >= 96)
        {
            tile.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(4, 10, 4, 3),
                Background = new LinearGradientBrush(Color.FromArgb(0xC0, 0, 0, 0), Colors.Transparent, 90),
                Child = new TextBlock { Text = summary.Name, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
            });
        }

        if (_zoom >= 128)
        {
            tile.Children.Add(new TextBlock
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"{summary.Origin.Width}×{summary.Origin.Height}"),
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(4, 1, 4, 1),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }

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
        if (_desk.Open is not { } open || open.Shelf.Scene(name) is not { } saved)
        {
            return;
        }

        var names = open.Store.Entries.ToDictionary(entry => entry.AssetId, entry => entry.Name, StringComparer.Ordinal);
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

        if (_desk.Open is not { } open)
        {
            return;
        }

        switch (open.Shelf.RenameScene(name, typed))
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

        if (answer == MessageBoxResult.Yes && _desk.Open is { } open && open.Shelf.DeleteScene(name))
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
