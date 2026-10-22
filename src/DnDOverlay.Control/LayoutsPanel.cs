using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The tab "Layouts" (Part 7): the saving with the table's reach - several screens at one grip. Chosen
/// by what it looks like, so a tile shows the screens side by side, each with its scene in miniature;
/// a generic symbol with a name would make the list a guessing game, because a layout is recognised by
/// what lay where.
/// <para>
/// <b>The number on top is the number of screens</b> - which is also the number of its scenes, since a
/// layout holds exactly one per screen and no empty ones. Written as screens, because that is the
/// question before loading: how much of my table does this take?
/// </para>
/// <para>
/// <b>Loading matches the saved screens against the ones there</b> - by address, then by name - and only
/// when something does not fit does the mapping table appear (Part 3). A layout whose screens all stand
/// there loads without a dialog.
/// </para>
/// </summary>
internal sealed class LayoutsPanel : Grid
{
    private readonly ISessionApi _session;
    private readonly CampaignDesk _desk;
    private readonly Pictures _pictures;
    private readonly Func<IReadOnlyList<ScreenView>> _screens;
    private readonly Action<string> _say;
    private readonly ILogger _logger;
    private readonly ControlSettings _settings;
    private readonly WrapPanel _tiles = new();
    private readonly ScrollViewer _scroller;
    private readonly PanelTouch _touch;
    private readonly TextBlock _empty = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        Margin = new Thickness(4),
    };

    private string _filter = string.Empty;
    private PanelSort _sort = PanelSort.Newest;
    private double _zoom = 192;

    internal LayoutsPanel(
        ISessionApi session,
        CampaignDesk desk,
        Pictures pictures,
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

        // Like a scene: no selection, a double tap loads, the long press opens the menu, and pulled
        // out it loads wherever it is let go over the stage.
        _touch = new PanelTouch(_scroller, surface, carry);
        _touch.DoubleTapped += async (_, tile) =>
        {
            if (tile.Tag is string name)
            {
                await LoadAsync(name, _settings.Current.LayoutLoad).ConfigureAwait(true);
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

    /// <summary>Scenes were saved out of a layout - the tab "Scenes" has new entries.</summary>
    internal event EventHandler? ScenesSaved;

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
            _empty.Text = Strings.LayoutsNoCampaign;
            _empty.Visibility = Visibility.Visible;
            return;
        }

        var layouts = open.Shelf.Layouts.Where(summary => NameFilter.Matches(NameFilter.Normalise(summary.Name), _filter));

        layouts = _sort switch
        {
            PanelSort.Oldest => layouts.OrderBy(summary => summary.SavedAt),
            PanelSort.NameAscending => layouts.OrderBy(summary => summary.Name, StringComparer.CurrentCultureIgnoreCase),
            PanelSort.NameDescending => layouts.OrderByDescending(summary => summary.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => layouts.OrderByDescending(summary => summary.SavedAt),
        };

        foreach (var summary in layouts)
        {
            if (open.Shelf.Layout(summary.Name) is { } layout)
            {
                _tiles.Children.Add(Tile(summary, layout));
            }
        }

        _empty.Text = open.Shelf.Layouts.Count == 0
            ? Strings.LayoutsNoneSaved
            : Strings.LayoutsNoMatch;
        _empty.Visibility = _tiles.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Saves the table as a layout (Part 3): every screen with something on it - inactive ones and
    /// those of a device that is off included, because their arrangement lies in the hub and can be
    /// played. Empty screens not: an entry "nothing lay here" would either do nothing when loaded or
    /// clear silently, and the absence says that better. The suggestion is <c>Layout &lt;time&gt;</c>;
    /// a taken name asks before it overwrites.
    /// </summary>
    /// <summary>
    /// "Save layout" in the quit dialog (Part 7): kept under the default name <c>Layout &lt;stamp&gt;</c>
    /// <b>without asking for one</b> - whoever quits does not want to name; renaming is for later, in
    /// the tab. An unnamed campaign is still asked for its name, as for any first save.
    /// </summary>
    /// <returns>The name it was kept under, or <see langword="null"/> when nothing was saved.</returns>
    internal async Task<string?> SaveOnQuitAsync(Window owner)
    {
        if (_desk.Open is null)
        {
            return null;
        }

        var parts = await PartsAsync().ConfigureAwait(true);

        if (parts.Count == 0 || (_desk.Open.Unnamed && !CampaignNaming.Ask(owner, _desk)) || _desk.Open is not { } open)
        {
            return null;
        }

        var name = FileNames.Suggest(Strings.LayoutsSuggestedName(FileNames.Stamp(DateTimeOffset.Now)));

        if (open.Shelf.SaveLayout(name, parts, overwrite: false) is not ShelfOutcome.Done)
        {
            return null;
        }

        ControlLog.LayoutSaved(_logger, name, parts.Count);

        return name;
    }

    /// <summary>Every screen with something on it, as the parts of a layout.</summary>
    private async Task<List<LayoutPart>> PartsAsync()
    {
        var parts = new List<LayoutPart>();

        foreach (var view in _screens())
        {
            var scene = await _session.GetSceneAsync(view.Screen, CancellationToken.None).ConfigureAwait(true);

            if (scene.Items.Count > 0 || scene.Background is not null)
            {
                parts.Add(new LayoutPart(view.Screen, view.Info.Label, new SavedScene(view.Info.Size, DateTimeOffset.Now, scene)));
            }
        }

        return parts;
    }

    internal async Task SaveAsync(Window owner, string? overwriting = null)
    {
        if (_desk.Open is null)
        {
            _say(Strings.LayoutsNoCampaignToSave);
            return;
        }

        var parts = await PartsAsync().ConfigureAwait(true);

        if (parts.Count == 0)
        {
            _say(Strings.LayoutsNothingToSave);
            return;
        }

        if (_desk.Open.Unnamed && !CampaignNaming.Ask(owner, _desk))
        {
            return;
        }

        if (_desk.Open is not { } open)
        {
            return;
        }

        var name = overwriting ?? NameDialog.Ask(
            owner,
            Strings.LayoutsSaveTitle,
            Strings.LayoutsSavePrompt(parts.Count),
            FileNames.Suggest(Strings.LayoutsSuggestedName(FileNames.Stamp(DateTimeOffset.Now))));

        if (name is null)
        {
            return;
        }

        var outcome = open.Shelf.SaveLayout(name, parts, overwrite: overwriting is not null);

        if (outcome is ShelfOutcome.Taken)
        {
            var answer = MessageBox.Show(
                owner,
                Strings.LayoutsOverwriteQuestion(name),
                Strings.LayoutsSaveTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            outcome = open.Shelf.SaveLayout(name, parts, overwrite: true);
        }

        if (outcome is ShelfOutcome.Done)
        {
            ControlLog.LayoutSaved(_logger, name, parts.Count);
            _say(Strings.LayoutsSaved(name, parts.Count));
            Refresh();
        }
    }

    /// <summary>
    /// Loads a layout: the saved screens matched against the ones there, the mapping table when they do
    /// not all fit, the names of the pictures resolved afresh, and the whole of it as ONE command.
    /// </summary>
    internal async Task LoadAsync(string name, LayoutLoad mode)
    {
        if (_desk.Open is not { } open || open.Shelf.Layout(name) is not { } layout)
        {
            return;
        }

        var screens = _screens();
        var found = LayoutMatching.Match(layout.Parts, [.. screens.Select(view => new LayoutScreen(view.Screen, view.Info.Label))]);

        IReadOnlyList<LayoutChoice> choices;

        if (found.Any(screen => screen is null))
        {
            if (LayoutMappingWindow.Ask(Window.GetWindow(this)!, name, layout.Parts, found, screens, _pictures) is not { } chosen)
            {
                return;
            }

            choices = chosen;
        }
        else
        {
            choices = [.. layout.Parts.Select((part, index) => new LayoutChoice(part, found[index], AsScene: false))];
        }

        var saved = SaveAsScenes([.. choices.Where(choice => choice.AsScene).Select(choice => choice.Part)]);
        var names = open.Store.Entries.ToDictionary(entry => entry.AssetId, entry => entry.Name, StringComparer.Ordinal);
        var laying = new List<LayoutScene>();
        var missing = 0;

        foreach (var choice in choices)
        {
            if (choice.Onto is not { } screen)
            {
                continue;
            }

            var (scene, absent) = SceneNames.Resolve(choice.Part.Scene.Scene, asset => names.GetValueOrDefault(asset.Value));

            laying.Add(new LayoutScene(screen, scene));
            missing += absent.Count;
        }

        if (laying.Count > 0)
        {
            await _session.LoadLayoutAsync(laying, mode, CancellationToken.None).ConfigureAwait(true);
        }

        var skipped = choices.Count(choice => choice.Onto is null && !choice.AsScene);
        var how = mode.ToString();

        ControlLog.LayoutLoaded(_logger, name, how, laying.Count, skipped, saved.Count, missing);

        var said = laying.Count == 0
            ? Strings.LayoutsNothingLaid(name)
            : Strings.LayoutsLoaded(name, laying.Select(scene => scene.Screen).Distinct().Count());

        if (skipped > 0)
        {
            said += " " + Strings.LayoutsScenesLeftOut(skipped);
        }

        if (saved.Count > 0)
        {
            said += " " + Strings.LayoutsSavedAsScenes(string.Join(", ", saved));
        }

        if (missing > 0)
        {
            said += " " + Strings.LayoutsPicturesMissing(missing);
        }

        _say(said);
    }

    /// <summary>
    /// Takes scenes out of a layout into the tab "Scenes", one per screen, named
    /// <c>Scene &lt;screen&gt; &lt;time&gt;</c> - numbered where that is taken, so no question interrupts
    /// a load. <b>The one place this happens</b> (Part 7): the menu's "Save as single scenes" and the
    /// mapping table's "Save as scene" both come here. Returns the names given.
    /// </summary>
    private List<string> SaveAsScenes(IReadOnlyList<LayoutPart> parts)
    {
        var saved = new List<string>();

        if (parts.Count == 0 || _desk.Open is not { } open)
        {
            return saved;
        }

        var stamp = FileNames.Stamp(DateTimeOffset.Now);

        foreach (var part in parts)
        {
            var name = FileNames.Numbered(
                FileNames.Suggest(Strings.LayoutsSceneName(part.ScreenName, stamp)),
                open.Shelf.Scenes.Select(scene => scene.Name));

            if (open.Shelf.SaveScene(name, part.Scene.Origin, part.Scene.Scene, overwrite: false) is ShelfOutcome.Done)
            {
                ControlLog.SceneSaved(_logger, name, part.Scene.Scene.Items.Count);
                saved.Add(name);
            }
        }

        if (saved.Count > 0)
        {
            ScenesSaved?.Invoke(this, EventArgs.Empty);
        }

        return saved;
    }

    private Grid Tile(LayoutSummary summary, SavedLayout layout)
    {
        // The screens side by side, each in its saved shape, sharing the tile's width.
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var room = (_zoom * 0.92) / Math.Max(1, layout.Parts.Count);

        foreach (var part in layout.Parts)
        {
            var origin = part.Scene.Origin;
            var aspect = origin.Height > 0 ? Math.Max(0.1, (double)origin.Width / origin.Height) : 16d / 9;
            var width = Math.Min(room - 4, _zoom * 0.6 * aspect);
            var preview = new SceneThumbnail(_pictures)
            {
                Width = Math.Max(8, width),
                Height = Math.Max(8, width / aspect),
                Margin = new Thickness(2),
            };

            preview.Show(part.Scene.Scene, ScreenContext.Default(origin, 96), default);
            preview.InvalidateVisual();
            row.Children.Add(preview);
        }

        var tile = new Grid
        {
            Width = _zoom,
            Height = _zoom * 0.75,
            Margin = new Thickness(0, 0, 6, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            ClipToBounds = true,
            Tag = summary.Name,
            ToolTip = Strings.LayoutsTileTip(summary.Name, string.Join(", ", summary.Screens), summary.SavedAt.LocalDateTime),
        };

        tile.Children.Add(row);

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

        tile.Children.Add(new TextBlock
        {
            Text = layout.Parts.Count == 1 ? Strings.LayoutsTileScreen(layout.Parts.Count) : Strings.LayoutsTileScreens(layout.Parts.Count),
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(4, 1, 4, 1),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        _touch.Card(tile, () => new CarryLoad([], Scene: null, Snapshot(row), Layout: summary.Name));

        tile.MouseLeftButtonDown += async (_, down) =>
        {
            if (down.ClickCount == 2 && down.StylusDevice is null)
            {
                down.Handled = true;
                await LoadAsync(summary.Name, _settings.Current.LayoutLoad).ConfigureAwait(true);
            }
        };

        tile.ContextMenu = Menu(summary.Name);
        tile.ContextMenuOpening += (_, _) => tile.ContextMenu = Menu(summary.Name);

        return tile;
    }

    /// <summary>
    /// The entry's menu (Part 7): the three ways to load · save as single scenes · rename · overwrite
    /// with the table as it stands · and, set apart, delete.
    /// </summary>
    private ContextMenu Menu(string name)
    {
        var menu = new ContextMenu();
        var load = new MenuItem { Header = Strings.LayoutsMenuLoad };

        load.Items.Add(Entry(Strings.LayoutsMenuAdd, () => _ = LoadAsync(name, LayoutLoad.Add)));
        load.Items.Add(Entry(Strings.LayoutsMenuReplaceUsed, () => _ = LoadAsync(name, LayoutLoad.ReplaceUsed)));
        load.Items.Add(Entry(Strings.LayoutsMenuReplaceAll, () => _ = LoadAsync(name, LayoutLoad.ReplaceAll)));

        menu.Items.Add(load);
        menu.Items.Add(Entry(Strings.LayoutsMenuSaveAsScenes, () => SaveAsScenesOf(name)));
        menu.Items.Add(Entry(Strings.LayoutsMenuRename, () => Rename(name)));
        menu.Items.Add(Entry(Strings.LayoutsMenuOverwrite, () => _ = SaveAsync(Window.GetWindow(this)!, overwriting: name)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Strings.LayoutsMenuDelete, () => Delete(name)));

        return menu;
    }

    private void SaveAsScenesOf(string name)
    {
        if (_desk.Open?.Shelf.Layout(name) is not { } layout)
        {
            return;
        }

        var saved = SaveAsScenes(layout.Parts);

        _say(saved.Count == 0 ? Strings.LayoutsNoSceneSaved : Strings.LayoutsSavedAsScenes(string.Join(", ", saved)));
    }

    private void Rename(string name)
    {
        if (NameDialog.Ask(Window.GetWindow(this)!, Strings.LayoutsRenameTitle, Strings.LayoutsRenamePrompt, name) is not { } typed)
        {
            return;
        }

        if (_desk.Open is not { } open)
        {
            return;
        }

        switch (open.Shelf.RenameLayout(name, typed))
        {
            case ShelfOutcome.Done:
                Refresh();
                break;

            case ShelfOutcome.Taken:
                _say(Strings.LayoutsRenameTaken(typed));
                break;

            default:
                break;
        }
    }

    private void Delete(string name)
    {
        var answer = MessageBox.Show(
            Window.GetWindow(this)!,
            Strings.LayoutsDeleteQuestion(name),
            Strings.LayoutsDeleteTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes && _desk.Open is { } open && open.Shelf.DeleteLayout(name))
        {
            Refresh();
        }
    }

    /// <summary>What the ghost shows while a layout is carried: its row of screens as it stands in the tile.</summary>
    private static RenderTargetBitmap? Snapshot(FrameworkElement row)
    {
        if (row.ActualWidth < 1 || row.ActualHeight < 1)
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(row);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(row.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(row.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        bitmap.Render(row);
        bitmap.Freeze();

        return bitmap;
    }

    private static MenuItem Entry(string header, Action chosen)
    {
        var item = new MenuItem { Header = header };

        item.Click += (_, _) => chosen();

        return item;
    }
}
