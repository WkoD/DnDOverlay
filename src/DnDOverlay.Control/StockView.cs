using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;
using CorePoint = DnDOverlay.Core.Point;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// The tab "Pictures" (Part 7): the stock of the open campaign as a grid - the picture IS the label,
/// and the same area holds three to five times what a list would. Everything descriptive lies ON the
/// picture: the name at the bottom, the resolution top left, the marks top right.
/// <para>
/// <b>Selection as on the stage, without a mode</b>: a click picks one, Ctrl+click adds, and with the
/// mouse a frame drawn from free area collects. With a finger there is no multiple selection (DM,
/// 19.10.2026): the plain drag belongs to scrolling. <b>The selection never reaches past what is
/// seen</b> - a filter change drops what fell out, or a delete of "12 entries" would take pictures
/// nobody sees (Part 7).
/// </para>
/// <para>
/// <b>A finger taps, double taps, holds, scrolls and pulls out</b> - <see cref="PanelTouch"/> tells
/// them apart; the mouse keeps its click, double click, Ctrl+click, frame and right button.
/// </para>
/// </summary>
internal sealed class StockView : Grid
{
    private readonly ISessionApi _session;
    private readonly CampaignDesk _desk;
    private readonly Pictures _pictures;
    private readonly Func<ScreenRef?> _active;
    private readonly Func<IReadOnlyList<ScreenView>> _screens;
    private readonly Action<string> _say;
    private readonly StockEdits _edits;

    private readonly WrapPanel _tiles = new() { Background = Brushes.Transparent };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Rectangle _frame = new()
    {
        Stroke = Brushes.SteelBlue,
        StrokeThickness = 1,
        Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x46, 0x82, 0xB4)),
        Visibility = Visibility.Collapsed,
    };

    private readonly ScrollViewer _scroller;
    private readonly PanelTouch _touch;
    private readonly List<AssetId> _selection = [];
    private readonly Dictionary<AssetId, Border> _cards = [];

    private string _filter = string.Empty;
    private PanelSort _sort = PanelSort.Newest;
    private double _zoom = 128;
    private IReadOnlyDictionary<AssetId, int> _lying = new Dictionary<AssetId, int>();
    private IReadOnlyDictionary<AssetId, int> _used = new Dictionary<AssetId, int>();
    private TilePoint? _frameFrom;

    internal StockView(
        ISessionApi session,
        CampaignDesk desk,
        Pictures pictures,
        Func<ScreenRef?> active,
        Func<IReadOnlyList<ScreenView>> screens,
        Action<string> say,
        PanelCarry carry,
        StockEdits edits)
    {
        _session = session;
        _desk = desk;
        _pictures = pictures;
        _active = active;
        _screens = screens;
        _say = say;
        _edits = edits;

        // It takes the keyboard when an entry is pressed, so Del can tell "the stock" from "the stage"
        // by focus (Part 7) - the key goes to whatever has focus, not to whatever the hand is over.
        Focusable = true;
        FocusVisualStyle = null;

        _overlay.Children.Add(_frame);

        var surface = new Grid { Background = Brushes.Transparent };

        surface.Children.Add(_tiles);
        surface.Children.Add(_overlay);

        _scroller = new ScrollViewer
        {
            Content = surface,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.Both,
        };

        Children.Add(_scroller);

        // With a finger a tap picks one and nothing more - no multiple selection (DM, 19.10.2026).
        _touch = new PanelTouch(_scroller, surface, carry);
        _touch.Tapped += (_, card) =>
        {
            if (card.Tag is InventoryEntry entry)
            {
                Select([new AssetId(entry.AssetId)]);
                Focus();
            }
        };
        _touch.DoubleTapped += async (_, card) =>
        {
            if (card.Tag is InventoryEntry entry)
            {
                var asset = new AssetId(entry.AssetId);

                await SendAsync(_active(), _selection.Contains(asset) ? Chosen : [Ref(entry)]).ConfigureAwait(true);
            }
        };
        _touch.Held += (_, card) =>
        {
            if (card is Border border && border.Tag is InventoryEntry entry)
            {
                OpenMenu(border, entry);
            }
        };

        // The frame starts on free area only, and only with the mouse (Part 7).
        surface.MouseLeftButtonDown += (_, down) =>
        {
            if (down.StylusDevice is not null || down.OriginalSource is not (Grid or WrapPanel))
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                Select([]);
            }

            _frameFrom = down.GetPosition(_overlay);
            _ = surface.CaptureMouse();
            down.Handled = true;
        };

        surface.MouseMove += (_, moved) =>
        {
            if (_frameFrom is not { } from)
            {
                return;
            }

            var to = moved.GetPosition(_overlay);
            var rect = new TileRect(from, to);

            Canvas.SetLeft(_frame, rect.Left);
            Canvas.SetTop(_frame, rect.Top);
            _frame.Width = rect.Width;
            _frame.Height = rect.Height;
            _frame.Visibility = Visibility.Visible;
        };

        surface.MouseLeftButtonUp += (_, up) =>
        {
            if (_frameFrom is not { } from)
            {
                return;
            }

            surface.ReleaseMouseCapture();
            _frameFrom = null;
            _frame.Visibility = Visibility.Collapsed;

            var rect = new TileRect(from, up.GetPosition(_overlay));

            if (rect.Width < Press.Tolerance && rect.Height < Press.Tolerance)
            {
                return;
            }

            foreach (var (asset, card) in _cards)
            {
                var bounds = card.TransformToVisual(_overlay).TransformBounds(new TileRect(card.RenderSize));

                if (bounds.IntersectsWith(rect) && !_selection.Contains(asset))
                {
                    _selection.Add(asset);
                }
            }

            Mark();
        };
    }

    /// <summary>The pictures picked out, in the order they were chosen - what "send to all" takes.</summary>
    internal IReadOnlyList<AssetRef> Chosen =>
        _desk.Open is { } open
            ? [.. _selection.Select(asset => open.Store.Entries.FirstOrDefault(entry => entry.AssetId == asset.Value))
                .OfType<InventoryEntry>()
                .Select(Ref)]
            : [];

    /// <summary>The selection changed - the menu's "send to all" follows it.</summary>
    internal event EventHandler? SelectionChanged;

    /// <summary>The finger's swipe toward the window's edge - a retracted panel goes back.</summary>
    internal event EventHandler? SwipedIn
    {
        add => _touch.SwipedIn += value;
        remove => _touch.SwipedIn -= value;
    }

    /// <summary>
    /// The one picture the grid is narrowed to by "Show in stock" (Part 7) - stronger than the filter
    /// text, which would also find every name the picture's name is part of. Null shows by the filter.
    /// </summary>
    internal AssetId? Narrowing { get; set; }

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

    /// <summary>Which pictures lie on how many screens - the mark that answers "have I shown that already?".</summary>
    internal async Task CountAsync()
    {
        var counts = new Dictionary<AssetId, int>();

        foreach (var view in _screens())
        {
            var scene = await _session.GetSceneAsync(view.Screen, CancellationToken.None).ConfigureAwait(true);

            foreach (var asset in scene.Items.OfType<ImageItem>().Select(item => item.AssetId).Distinct())
            {
                counts[asset] = counts.GetValueOrDefault(asset) + 1;
            }
        }

        _lying = counts;
        Refresh();
    }

    /// <summary>Draws the grid again from the open campaign.</summary>
    internal void Refresh()
    {
        _tiles.Children.Clear();
        _cards.Clear();
        _touch.Forget();

        if (_desk.Open is not { } open)
        {
            _tiles.Children.Add(new TextBlock { Text = "No campaign open.", Opacity = 0.7, Margin = new Thickness(4) });
            Select([]);
            return;
        }

        var entries = open.Store.Entries
            .Select(entry => (Entry: entry, Name: NameFilter.Normalise(entry.Name)))
            .Where(pair => Narrowing is { } only
                ? pair.Entry.AssetId == only.Value
                : NameFilter.Matches(pair.Name, _filter))
            .Select(pair => pair.Entry);

        entries = _sort switch
        {
            PanelSort.Oldest => entries.OrderBy(entry => entry.AddedAt),
            PanelSort.NameAscending => entries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            PanelSort.NameDescending => entries.OrderByDescending(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => entries.OrderByDescending(entry => entry.AddedAt),
        };

        var missing = open.Store.Missing;

        _used = open.Shelf.UseCounts();

        foreach (var entry in entries)
        {
            var card = Card(entry, missing.Contains(new AssetId(entry.AssetId)));

            _cards[new AssetId(entry.AssetId)] = card;
            _tiles.Children.Add(card);
        }

        if (_cards.Count == 0)
        {
            _tiles.Children.Add(new TextBlock
            {
                Text = open.Store.Count == 0 ? "The stock is empty - drop pictures here, paste or fetch an address." : "Nothing matches the filter.",
                Opacity = 0.7,
                Margin = new Thickness(4),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // What fell out of view falls out of the selection (Part 7).
        Select([.. _selection.Where(_cards.ContainsKey)]);
    }

    /// <summary>
    /// Picks a picture out and brings it into view with a short highlight - the end of "Show in
    /// stock" (Part 7). After the layout pass, or there is nothing yet to scroll to.
    /// </summary>
    internal void Reveal(AssetId asset)
    {
        if (!_cards.TryGetValue(asset, out var card))
        {
            return;
        }

        Select([asset]);

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            card.BringIntoView();
            card.BorderBrush = Brushes.Orange;

            var fading = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };

            fading.Tick += (_, _) =>
            {
                fading.Stop();
                Mark();
            };
            fading.Start();
        });
    }

    private Border Card(InventoryEntry entry, bool missing)
    {
        var asset = new AssetId(entry.AssetId);
        var grid = new Grid { Width = _zoom, Height = _zoom, Background = Brushes.Black, ClipToBounds = true };

        grid.Children.Add(new Image { Source = _pictures.For(asset), Stretch = Stretch.Uniform });

        // Below a size the overlays go - first the name, then the marks; the picture alone carries
        // the recognition, and the name stands in the tooltip (Part 7).
        if (_zoom >= 96)
        {
            grid.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(4, 10, 4, 3),
                Background = new LinearGradientBrush(Color.FromArgb(0xC0, 0, 0, 0), Colors.Transparent, 90),
                Child = new TextBlock
                {
                    Text = entry.Name,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            });
        }

        if (_zoom >= 64)
        {
            var marks = new List<string>();

            if (missing)
            {
                marks.Add("missing");
            }

            if (entry.IsAnimated)
            {
                marks.Add("GIF");
            }

            if (_lying.GetValueOrDefault(asset) is var on and > 0)
            {
                marks.Add(string.Create(CultureInfo.CurrentCulture, $"on {on}"));
            }

            // Whether something saved needs it, before anybody clicks (Part 7): "in 3" is three scenes
            // or layouts. A picture nothing needs carries no mark and looks different for it.
            if (_used.GetValueOrDefault(asset) is var used and > 0)
            {
                marks.Add(string.Create(CultureInfo.CurrentCulture, $"in {used}"));
            }

            var top = new DockPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
                Background = new LinearGradientBrush(Color.FromArgb(0xA0, 0, 0, 0), Colors.Transparent, 90) { RelativeTransform = new RotateTransform(180, 0.5, 0.5) },
            };

            var right = new TextBlock { Text = string.Join(" · ", marks), Foreground = Brushes.White, Margin = new Thickness(3, 1, 4, 1), FontSize = 11 };

            DockPanel.SetDock(right, Dock.Right);
            top.Children.Add(right);

            if (_zoom >= 128)
            {
                top.Children.Add(new TextBlock
                {
                    Text = string.Create(CultureInfo.InvariantCulture, $"{entry.PixelWidth}×{entry.PixelHeight}"),
                    Foreground = Brushes.White,
                    Margin = new Thickness(4, 1, 3, 1),
                    FontSize = 11,
                });
            }

            grid.Children.Add(top);
        }

        var card = new Border
        {
            Child = grid,
            Margin = new Thickness(0, 0, 6, 6),
            BorderThickness = new Thickness(3),
            BorderBrush = Brushes.Transparent,
            ToolTip = entry.Name,
            Tag = entry,
        };

        // Out of the grid it carries the selection when the entry is part of it, else the entry alone
        // (Part 7: "dragging drags the whole selection").
        _touch.Card(card, () =>
        {
            IReadOnlyList<AssetRef> taken = _selection.Contains(asset) ? Chosen : [Ref(entry)];

            return taken.Count == 0 ? null : new CarryLoad(taken, Scene: null, _pictures.For(asset));
        });

        // A press on a picture that is part of a selection keeps the selection - it may be the start
        // of carrying all of them - and narrows it to this one only if the button comes up unmoved.
        var narrowOnRelease = false;

        card.MouseLeftButtonDown += async (_, down) =>
        {
            down.Handled = true;
            narrowOnRelease = false;

            if (down.StylusDevice is not null)
            {
                return;
            }

            Focus();

            if (down.ClickCount == 2)
            {
                await SendAsync(_active(), _selection.Contains(asset) ? Chosen : [Ref(entry)]).ConfigureAwait(true);
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (!_selection.Remove(asset))
                {
                    _selection.Add(asset);
                }

                Mark();
            }
            else if (_selection.Contains(asset) && _selection.Count > 1)
            {
                narrowOnRelease = true;
            }
            else
            {
                Select([asset]);
            }
        };

        card.MouseLeftButtonUp += (_, up) =>
        {
            if (narrowOnRelease && up.StylusDevice is null && !_touch.MouseCarrying)
            {
                Select([asset]);
            }

            narrowOnRelease = false;
        };

        card.ContextMenuOpening += (_, _) =>
        {
            if (!_selection.Contains(asset))
            {
                Select([asset]);
            }

            card.ContextMenu = Menu(entry);
        };

        card.ContextMenu = Menu(entry);

        return card;
    }

    /// <summary>The entry's menu opened by a finger's long press, on the entry it was pressed on.</summary>
    private void OpenMenu(Border card, InventoryEntry entry)
    {
        if (!_selection.Contains(new AssetId(entry.AssetId)))
        {
            Select([new AssetId(entry.AssetId)]);
        }

        var menu = Menu(entry);

        menu.PlacementTarget = card;
        card.ContextMenu = menu;
        menu.IsOpen = true;
    }

    /// <summary>
    /// The entry's menu (Part 7): send to the active screen, as a picture or as the background to
    /// any screen or to all, where it lies now, the file in its folder. One entry or the whole
    /// selection; what makes sense for one only is greyed for several.
    /// </summary>
    private ContextMenu Menu(InventoryEntry entry)
    {
        var several = _selection.Count > 1;
        var menu = new ContextMenu();

        menu.Items.Add(Entry("Send to active screen", () => _ = SendAsync(_active(), Chosen.Count > 0 ? Chosen : [Ref(entry)])));

        var asPicture = new MenuItem { Header = "Send as picture" };
        var asBackground = new MenuItem { Header = "Send as background", IsEnabled = !several };

        foreach (var view in _screens())
        {
            var screen = view.Screen;

            asPicture.Items.Add(Entry(view.Info.Label, () => _ = SendAsync(screen, Chosen.Count > 0 ? Chosen : [Ref(entry)]), active: screen == _active()));
            asBackground.Items.Add(Entry(view.Info.Label, () => _ = _session.SetBackgroundAsync(screen, Ref(entry)), active: screen == _active()));
        }

        // "To all" is the last entry IN the list of screens, not beside it - the same act with the
        // target "all" (Part 7).
        asPicture.Items.Add(new Separator());
        asPicture.Items.Add(Entry("To all", () => _ = _session.SendToAllAsync(Chosen.Count > 0 ? Chosen : [Ref(entry)])));
        asBackground.Items.Add(new Separator());
        asBackground.Items.Add(Entry("To all", () => _ = _session.SetBackgroundOnAllAsync(Ref(entry))));

        menu.Items.Add(asPicture);
        menu.Items.Add(asBackground);

        var asset = new AssetId(entry.AssetId);
        var lying = _lying.GetValueOrDefault(asset);

        menu.Items.Add(new MenuItem
        {
            Header = several ? "Lies on ..." : lying == 0 ? "Lies on no screen" : string.Create(CultureInfo.CurrentCulture, $"Lies on {lying} screen(s)"),
            IsEnabled = false,
        });

        menu.Items.Add(Entry("Show file in folder", () => ShowFile(entry), enabled: !several));
        menu.Items.Add(Used(entry, several));
        menu.Items.Add(Entry("Rename ...", () => _ = _edits.RenameAsync(Window.GetWindow(this)!, asset), enabled: !several));

        // Set apart at the foot: the one entry here that cannot be taken back.
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(
            several ? string.Create(CultureInfo.CurrentCulture, $"Delete {_selection.Count} pictures ...") : "Delete ...",
            () => _ = DeleteChosenAsync(entry)));

        return menu;
    }

    /// <summary>Deletes the selection, or the one entry when nothing is chosen - the menu's and the Del key's way in.</summary>
    internal Task DeleteChosenAsync(InventoryEntry? pressed = null)
    {
        IReadOnlyList<AssetId> assets = _selection.Count > 0
            ? [.. _selection]
            : pressed is null ? [] : [new AssetId(pressed.AssetId)];

        return assets.Count == 0 ? Task.CompletedTask : _edits.DeleteAsync(Window.GetWindow(this)!, assets);
    }

    /// <summary>
    /// "Used in" (Part 7): the scenes and layouts that show the picture, by name - unfolded from the
    /// mark on the tile. One entry only; for several it would be a list of lists.
    /// </summary>
    private MenuItem Used(InventoryEntry entry, bool several)
    {
        if (several || _desk.Open is not { } open)
        {
            return new MenuItem { Header = "Used in ...", IsEnabled = false };
        }

        var usage = open.Shelf.UsageOf(new AssetId(entry.AssetId));

        if (!usage.Used)
        {
            return new MenuItem { Header = "Used in no scene or layout", IsEnabled = false };
        }

        var used = new MenuItem { Header = "Used in" };

        foreach (var scene in usage.Scenes)
        {
            used.Items.Add(new MenuItem { Header = $"Scene: {scene}", IsEnabled = false });
        }

        foreach (var layout in usage.Layouts)
        {
            used.Items.Add(new MenuItem { Header = $"Layout: {layout}", IsEnabled = false });
        }

        return used;
    }

    private async Task SendAsync(ScreenRef? screen, IReadOnlyList<AssetRef> assets)
    {
        if (screen is not { } target)
        {
            _say("No screen to send to.");
            return;
        }

        if (assets.Count == 0)
        {
            return;
        }

        // ONE command, one patch, and Flow lays them side by side (Part 7).
        await _session.AddItemsAsync(target, assets).ConfigureAwait(true);
    }

    /// <summary>
    /// The only way from an entry to its file - files are named by their hash, the name the DM gave
    /// appears nowhere on disk (Part 7).
    /// </summary>
    private void ShowFile(InventoryEntry entry)
    {
        if (_desk.Open is not { } open)
        {
            return;
        }

        var file = System.IO.Path.Combine(open.Directory, "assets", entry.AssetId + "." + entry.Extension);

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
    }

    private void Select(IReadOnlyList<AssetId> assets)
    {
        _selection.Clear();
        _selection.AddRange(assets);
        Mark();
    }

    private void Mark()
    {
        foreach (var (asset, card) in _cards)
        {
            card.BorderBrush = _selection.Contains(asset) ? Brushes.SteelBlue : Brushes.Transparent;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static AssetRef Ref(InventoryEntry entry) => new(new AssetId(entry.AssetId), entry.Meta, entry.Name);

    private static MenuItem Entry(string header, Action chosen, bool enabled = true, bool active = false)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal };

        item.Click += (_, _) => chosen();

        return item;
    }
}
