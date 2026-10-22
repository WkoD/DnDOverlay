using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    private readonly RecordingSession _session;
    private readonly CampaignDesk _desk;
    private readonly Pictures _pictures;
    private readonly Func<ScreenRef?> _active;
    private readonly Func<IReadOnlyList<ScreenView>> _screens;
    private readonly Action<string> _say;
    private readonly ILogger _logger;
    private readonly ControlSettings _settings;
    private readonly WrapPanel _tiles = new();
    private readonly ScrollViewer _scroller;
    private readonly PanelTouch _touch;
    private readonly TextBlock _empty = new()
    {
        Text = Strings.ScenesNoneSaved,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        Margin = new Thickness(4),
    };

    private string _filter = string.Empty;
    private PanelSort _sort = PanelSort.Newest;
    private double _zoom = 160;

    internal ScenesPanel(
        RecordingSession session,
        CampaignDesk desk,
        Pictures pictures,
        Func<ScreenRef?> active,
        Func<IReadOnlyList<ScreenView>> screens,
        Action<string> say,
        ILogger logger,
        PanelCarry carry,
        ControlSettings settings)
    {
        _settings = settings;
        _session = session;
        _desk = desk;
        _pictures = pictures;
        _active = active;
        _screens = screens;
        _say = say;
        _logger = logger;

        var surface = new StackPanel { Children = { _empty, _tiles }, Background = Brushes.Transparent };

        _scroller = new ScrollViewer
        {
            Content = surface,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Children.Add(_scroller);

        // A scene is not selected - a tap does nothing, a double tap loads it onto the active screen,
        // the long press opens its menu, and pulled out it goes onto the tile it is dropped on.
        _touch = new PanelTouch(_scroller, surface, carry);
        _touch.DoubleTapped += async (_, tile) =>
        {
            if (tile.Tag is string name)
            {
                await LoadOnActiveAsync(name).ConfigureAwait(true);
            }
        };
        _touch.Held += (_, tile) =>
        {
            if (tile.Tag is string name)
            {
                var menu = Menu(name);

                menu.PlacementTarget = tile;
                tile.ContextMenu = menu;
                menu.IsOpen = true;
            }
        };

        Refresh();
    }

    /// <summary>The finger's swipe toward the window's edge - a retracted panel goes back.</summary>
    internal event EventHandler? SwipedIn
    {
        add => _touch.SwipedIn += value;
        remove => _touch.SwipedIn -= value;
    }

    /// <summary>Draws the grid for this filter, order, tile size and side of the panel.</summary>
    internal void Apply(string filter, PanelSort sort, double zoom, PanelSide side)
    {
        _filter = filter;
        _sort = sort;
        _zoom = zoom;
        _touch.Side = side;

        var lying = side is PanelSide.Top or PanelSide.Bottom;

        _tiles.Orientation = lying ? Orientation.Vertical : Orientation.Horizontal;
        _scroller.VerticalScrollBarVisibility = lying ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _scroller.HorizontalScrollBarVisibility = lying ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        Refresh();
    }

    /// <summary>Draws the list again - after a save, a rename, a delete.</summary>
    internal void Refresh()
    {
        _tiles.Children.Clear();
        _touch.Forget();

        if (_desk.Open is not { } open)
        {
            _empty.Text = Strings.ScenesNoCampaign;
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
            ? Strings.ScenesNoneSaved
            : Strings.ScenesNoMatch;
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
            _say(Strings.ScenesNoCampaignToSave);
            return;
        }

        // <b>Saving a scene names the campaign</b> (Part 3): an unnamed one is asked for its name
        // once, its folder renamed, and then the scene saved. No scene without a home.
        if (_desk.Open.Unnamed && !CampaignNaming.Ask(owner, _desk))
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

        // The suggestion is numbered when the stamp is taken - the same screen saved twice in one
        // minute would otherwise suggest a name that ends in the overwrite question (Part 11).
        var name = overwriting ?? NameDialog.Ask(
            owner,
            Strings.ScenesSaveTitle,
            Strings.ScenesSavePrompt(label),
            FileNames.Numbered(
                FileNames.Suggest(Strings.ScenesSuggestedName(label, FileNames.Stamp(DateTimeOffset.Now))),
                shelf.Scenes.Select(saved => saved.Name)));

        if (name is null)
        {
            return;
        }

        var scene = await _session.GetSceneAsync(screen, CancellationToken.None).ConfigureAwait(true);
        var origin = view?.Info.Size ?? new PixelSize(1920, 1080);

        if (!CampaignDesk.Shelves(() => shelf.SaveScene(name, origin, scene, overwrite: overwriting is not null), out var outcome, out var refused))
        {
            _say(Strings.ScenesWriteFailed(name, refused));
            return;
        }

        if (outcome is ShelfOutcome.Taken)
        {
            var answer = MessageBox.Show(
                owner,
                Strings.ScenesOverwriteQuestion(name),
                Strings.ScenesSaveTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            if (!CampaignDesk.Shelves(() => shelf.SaveScene(name, origin, scene, overwrite: true), out outcome, out refused))
            {
                _say(Strings.ScenesWriteFailed(name, refused));
                return;
            }
        }

        if (outcome is ShelfOutcome.Done)
        {
            ControlLog.SceneSaved(_logger, name, scene.Items.Count);
            _say(Strings.ScenesSaved(name));
            Refresh();
        }
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
            ToolTip = Strings.ScenesTileTip(summary.Name, summary.Origin.Width, summary.Origin.Height, summary.SavedAt.LocalDateTime),
        };

        tile.Children.Add(preview);

        tile.Tag = summary.Name;
        _touch.Card(tile, () => new CarryLoad([], summary.Name, Snapshot(preview)));

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

        // The double click; the double tap comes through PanelTouch, which takes the touch before it
        // could be promoted to a click.
        tile.MouseLeftButtonDown += async (_, down) =>
        {
            if (down.ClickCount == 2 && down.StylusDevice is null)
            {
                down.Handled = true;
                await LoadOnActiveAsync(summary.Name).ConfigureAwait(true);
            }
        };

        tile.ContextMenu = Menu(summary.Name);
        tile.ContextMenuOpening += (_, _) => tile.ContextMenu = Menu(summary.Name);

        return tile;
    }

    private ContextMenu Menu(string name)
    {
        var menu = new ContextMenu();
        var load = new MenuItem { Header = Strings.ScenesMenuLoad };
        var replace = new MenuItem { Header = Strings.ScenesMenuReplace };
        var add = new MenuItem { Header = Strings.ScenesMenuAdd };
        var overwrite = new MenuItem { Header = Strings.ScenesMenuOverwrite };

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
        menu.Items.Add(Entry(Strings.ScenesMenuRename, () => Rename(name)));
        menu.Items.Add(overwrite);
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Strings.ScenesMenuDelete, () => Delete(name)));

        return menu;
    }

    /// <summary>
    /// The double tap's load: the way set in the settings (replacing unless changed), onto the active
    /// screen (Part 7).
    /// </summary>
    private async Task LoadOnActiveAsync(string name)
    {
        if (_active() is { } screen)
        {
            await LoadAsync(name, screen, _settings.Current.SceneLoad).ConfigureAwait(true);
        }
        else
        {
            _say(Strings.ScenesNoScreen);
        }
    }

    /// <summary>
    /// What the ghost shows while a scene is carried: its preview as it stands in the tile. A copy,
    /// because the tile itself stays where it is.
    /// </summary>
    private static RenderTargetBitmap? Snapshot(FrameworkElement preview)
    {
        if (preview.ActualWidth < 1 || preview.ActualHeight < 1)
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(preview);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(preview.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(preview.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        bitmap.Render(preview);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>
    /// Loads a scene: the names resolved afresh from the campaign, the pictures it does not have
    /// counted for ONE message (Part 3), and the arrangement computed against the target by the
    /// hub (siting question 4).
    /// </summary>
    internal async Task LoadAsync(string name, ScreenRef screen, SceneLoad mode)
    {
        if (_desk.Open is not { } open || open.Shelf.Scene(name) is not { } saved)
        {
            return;
        }

        var names = open.Store.Entries.ToDictionary(entry => entry.AssetId, entry => entry.Name, StringComparer.Ordinal);
        var (scene, missing) = SceneNames.Resolve(saved.Scene, asset => names.GetValueOrDefault(asset.Value));

        await _session.LoadSavedSceneAsync(name, screen, scene, mode).ConfigureAwait(true);

        var pictures = scene.Items.OfType<ImageItem>().Select(item => item.AssetId)
            .Concat(scene.Background is { } background ? [background.AssetId] : [])
            .Distinct()
            .Count();

        var how = mode.ToString();

        ControlLog.SceneLoaded(_logger, name, how, scene.Items.Count, missing.Count);

        _say(missing.Count == 0
            ? Strings.ScenesLoaded(name)
            : Strings.ScenesLoadedMissing(name, missing.Count, pictures));
    }

    private void Rename(string name)
    {
        if (NameDialog.Ask(Window.GetWindow(this)!, Strings.ScenesRenameTitle, Strings.ScenesRenamePrompt, name) is not { } typed)
        {
            return;
        }

        if (_desk.Open is not { } open)
        {
            return;
        }

        if (!CampaignDesk.Shelves(() => open.Shelf.RenameScene(name, typed), out var outcome, out var refused))
        {
            _say(Strings.ScenesWriteFailed(name, refused));
            return;
        }

        switch (outcome)
        {
            case ShelfOutcome.Done:
                Refresh();
                break;

            case ShelfOutcome.Taken:
                _say(Strings.ScenesRenameTaken(typed));
                break;

            default:
                break;
        }
    }

    private void Delete(string name)
    {
        var answer = MessageBox.Show(
            Window.GetWindow(this)!,
            Strings.ScenesDeleteQuestion(name),
            Strings.ScenesDeleteTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes || _desk.Open is not { } open)
        {
            return;
        }

        if (!CampaignDesk.Removes(() => open.Shelf.DeleteScene(name), () => open.Shelf.Scene(name) is not null, out var refused))
        {
            _say(Strings.ScenesDeleteFailed(name, refused));
        }
        else
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
