using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DnDOverlay.Core.Configuration;
using Microsoft.Win32;

namespace DnDOverlay.Control;

/// <summary>
/// The campaign panel (Part 7): everything the DM BRINGS, in one dockable panel beside the stage.
/// Head, tabs, search bar, grid - in that order along the scrolling axis.
/// <para>
/// <b>"At the top" means at the start of the scrolling axis.</b> Docked left or right the panel is
/// upright: head, a row of tabs and the search bar above a grid that scrolls down. Docked top or
/// bottom it lies: the head on the left over the height, the tabs a column beside it, the search bar
/// still horizontal directly above the grid - it filters what it stands before - and the grid scrolls
/// sideways. The short axis decides: lying low, rows above the grid would eat two of its three rows.
/// </para>
/// <para>
/// <b>Search, order and zoom belong to all tabs</b> and act on the open one; only the zoom is kept per
/// tab, because pictures and scenes carry different detail.
/// </para>
/// </summary>
internal sealed class CampaignPanel : Border
{
    /// <summary>The tile sizes, thumbnail-small to a portrait one really looks at - up to the 256 px preview (Part 7).</summary>
    private static readonly double[] Zooms = [64, 96, 128, 160, 192, 256];

    private readonly ControlSettings _settings;
    private readonly StockView _stock;
    private readonly ScenesPanel _scenes;
    private readonly LayoutsPanel _layouts;
    private readonly IntakeRunner _intake;

    private readonly ToggleButton _picturesTab = Tab("Pictures");
    private readonly ToggleButton _scenesTab = Tab("Scenes");
    private readonly ToggleButton _layoutsTab = Tab("Layouts");

    /// <summary>"Save layout" at the end of the search bar of its tab - where the new entry appears right after (Part 7).</summary>
    private readonly Button _saveLayout = Small("Save layout ...");
    private readonly TextBox _search = new() { MinWidth = 120, Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _clear = Small("×");
    private readonly Button _sortButton = Small(string.Empty);
    private readonly Button _smaller = Small("−");
    private readonly Button _larger = Small("+");
    private readonly TextBox _address = new() { MinWidth = 140, Padding = new Thickness(4, 2, 4, 2) };
    private readonly Button _breakOff = Small("Break off");
    private readonly StackPanel _grips = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _footer = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private PanelSort _sort;
    private double _pictureZoom;
    private double _sceneZoom;
    private double _layoutZoom;
    private PanelTab _open;
    private PanelSide _side = PanelSide.Right;
    private string? _narrowedTo;

    internal CampaignPanel(
        PanelHead head,
        StockView stock,
        ScenesPanel scenes,
        LayoutsPanel layouts,
        IntakeRunner intake,
        ControlSettings settings)
    {
        Head = head;
        _stock = stock;
        _scenes = scenes;
        _layouts = layouts;
        _intake = intake;
        _settings = settings;

        var placement = settings.Current.Panel;

        _sort = placement?.Sort ?? PanelSort.Newest;
        _pictureZoom = placement?.PictureZoom ?? 128;
        _sceneZoom = placement?.SceneZoom ?? 160;
        _layoutZoom = placement?.LayoutZoom ?? 192;
        _open = placement?.Tab ?? PanelTab.Pictures;

        Padding = new Thickness(8);
        Background = SystemColors.ControlBrush;

        Check();
        _picturesTab.Click += (_, _) => Show(PanelTab.Pictures);
        _scenesTab.Click += (_, _) => Show(PanelTab.Scenes);
        _layoutsTab.Click += (_, _) => Show(PanelTab.Layouts);
        _saveLayout.Click += async (_, _) => await _layouts.SaveAsync(Window.GetWindow(this)!).ConfigureAwait(true);

        // A filter one cannot get rid of in one movement stays by accident, and then pictures are
        // suddenly missing: a tap on × and Escape both clear it (Part 7). A short debounce keeps the
        // grid from being rebuilt on every keystroke.
        _search.TextChanged += (_, _) =>
        {
            // Typing anything over "Show in stock"'s name goes back to filtering by text.
            if (_narrowedTo is not null && _search.Text != _narrowedTo)
            {
                _narrowedTo = null;
                _stock.Narrowing = null;
            }

            _typing.Stop();
            _typing.Start();
        };
        _typing.Tick += (_, _) =>
        {
            _typing.Stop();
            Apply();
        };
        _search.PreviewKeyDown += (_, key) =>
        {
            if (key.Key is Key.Escape)
            {
                _search.Clear();
                key.Handled = true;
            }
        };
        _clear.Click += (_, _) => _search.Clear();

        _stock.SwipedIn += (_, _) => SwipedIn?.Invoke(this, EventArgs.Empty);
        _scenes.SwipedIn += (_, _) => SwipedIn?.Invoke(this, EventArgs.Empty);
        _layouts.SwipedIn += (_, _) => SwipedIn?.Invoke(this, EventArgs.Empty);

        _sortButton.Click += (_, _) =>
        {
            _sort = (PanelSort)(((int)_sort + 1) % 4);
            Keep();
            Apply();
        };

        _smaller.Click += (_, _) => Zoom(-1);
        _larger.Click += (_, _) => Zoom(+1);

        // Ctrl+wheel zooms; on touch the buttons do, because a two-finger pinch would collide with
        // scrolling (Part 7).
        PreviewMouseWheel += (_, wheel) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Zoom(wheel.Delta > 0 ? +1 : -1);
                wheel.Handled = true;
            }
        };

        BuildFooter();

        // A drop onto the panel takes the pictures into the stock and nothing more - putting one on a
        // screen is a drop onto its tile (Part 7). No extension filter: the ingest decides what a
        // file was, or a .rptok would be locked out by the very layer meant to let it in.
        AllowDrop = true;
        Drop += async (_, e) => await _intake.RunAsync(_intake.Entrances.FromDataObject(e.Data), target: null).ConfigureAwait(true);
        DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };

        _intake.RunningChanged += (_, running) =>
        {
            _grips.IsEnabled = !running;
            _breakOff.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

            if (!running)
            {
                _stock.Refresh();
            }
        };

        Arrange();
        Apply();
    }

    /// <summary>The head: campaign, view, menu - the strip that stays whatever tab is open.</summary>
    internal PanelHead Head { get; }

    /// <summary>
    /// The edge the panel docks at. Top or bottom it lies along the edge rather than standing at a
    /// side; and every side says which way across is toward the stage (<see cref="Core.PanelGesture"/>).
    /// </summary>
    internal PanelSide Side
    {
        get => _side;
        set
        {
            if (value != _side)
            {
                _side = value;
                Arrange();
                Apply();
            }
        }
    }

    /// <summary>Whether the panel lies along the top or bottom edge.</summary>
    internal bool Lying => _side is PanelSide.Top or PanelSide.Bottom;

    /// <summary>A finger swiped toward the window's edge on one of the grids.</summary>
    internal event EventHandler? SwipedIn;

    /// <summary>
    /// "Show in stock" from a picture on a tile (Part 7): the tab Pictures, narrowed to this one
    /// picture whatever filter stood before, and scrolled to it - the way back from the table into
    /// the stock, where three hundred entries would otherwise mean reading a name and typing it. The
    /// name stands in the search bar, so it is visible why the grid shows one picture, and typing
    /// over it filters by text again.
    /// </summary>
    internal void ShowInStock(Core.AssetId asset, string name)
    {
        if (!PicturesOpen)
        {
            Show(PanelTab.Pictures);
        }

        _typing.Stop();
        _narrowedTo = name;
        _stock.Narrowing = asset;
        _search.Text = name;
        _typing.Stop();

        Apply();
        _stock.Reveal(asset);
    }

    /// <summary>Draws the open tab again - after a campaign change or a patch.</summary>
    internal void Refresh() => Apply();

    private void Show(PanelTab tab)
    {
        _open = tab;
        Check();
        Arrange();
        Keep();
        Apply();
    }

    /// <summary>The tabs are toggle buttons; a click on the open one must not leave it unchecked.</summary>
    private void Check()
    {
        _picturesTab.IsChecked = _open is PanelTab.Pictures;
        _scenesTab.IsChecked = _open is PanelTab.Scenes;
        _layoutsTab.IsChecked = _open is PanelTab.Layouts;
    }

    private bool PicturesOpen => _open is PanelTab.Pictures;

    private void Apply()
    {
        _sortButton.Content = _sort switch
        {
            PanelSort.Oldest => "Oldest ↑",
            PanelSort.NameAscending => "Name A–Z",
            PanelSort.NameDescending => "Name Z–A",
            _ => "Newest ↓",
        };

        switch (_open)
        {
            case PanelTab.Pictures:
                _stock.Apply(_narrowedTo is null ? _search.Text : string.Empty, _sort, _pictureZoom, _side);
                break;

            case PanelTab.Scenes:
                _scenes.Apply(_search.Text, _sort, _sceneZoom, _side);
                break;

            default:
                _layouts.Apply(_search.Text, _sort, _layoutZoom, _side);
                break;
        }
    }

    private void Zoom(int step)
    {
        var current = _open switch
        {
            PanelTab.Pictures => _pictureZoom,
            PanelTab.Scenes => _sceneZoom,
            _ => _layoutZoom,
        };
        var index = Array.FindIndex(Zooms, zoom => zoom >= current);
        var next = Zooms[Math.Clamp((index < 0 ? Zooms.Length - 1 : index) + step, 0, Zooms.Length - 1)];

        switch (_open)
        {
            case PanelTab.Pictures:
                _pictureZoom = next;
                break;

            case PanelTab.Scenes:
                _sceneZoom = next;
                break;

            default:
                _layoutZoom = next;
                break;
        }

        Keep();
        Apply();
    }

    /// <summary>Sort, zoom and the open tab are the DM's habits and kept across campaigns (Part 7).</summary>
    private void Keep() =>
        _settings.Update(current => current with
        {
            Panel = (current.Panel ?? new PanelPlacement(PanelSide.Right, 360)) with
            {
                Sort = _sort,
                PictureZoom = _pictureZoom,
                SceneZoom = _sceneZoom,
                LayoutZoom = _layoutZoom,
                Tab = _open,
            },
        });

    /// <summary>
    /// Lays the parts out for the panel's lie: upright, everything stacked above the grid; lying,
    /// head and tabs side by side on the left and the search bar over the grid.
    /// </summary>
    private void Arrange()
    {
        var lying = Lying;

        Head.Vertical = lying;

        Detach(Head, _picturesTab, _scenesTab, _layoutsTab, _stock, _scenes, _layouts, _footer);

        var tabs = new StackPanel { Orientation = lying ? Orientation.Vertical : Orientation.Horizontal, Margin = lying ? new Thickness(6, 0, 6, 0) : new Thickness(0, 6, 0, 6) };

        foreach (var tab in new[] { _picturesTab, _scenesTab, _layoutsTab })
        {
            // Upright tabs in a lying panel turn their label with them, still symbol AND word (Part 7).
            tab.LayoutTransform = lying ? new RotateTransform(-90) : Transform.Identity;
            tabs.Children.Add(tab);
        }

        var bar = SearchBar();
        var content = _open switch
        {
            PanelTab.Pictures => (FrameworkElement)_stock,
            PanelTab.Scenes => _scenes,
            _ => _layouts,
        };
        var column = new DockPanel();

        DockPanel.SetDock(bar, Dock.Top);
        column.Children.Add(bar);

        if (PicturesOpen)
        {
            DockPanel.SetDock(_footer, Dock.Bottom);
            column.Children.Add(_footer);
        }

        column.Children.Add(content);

        var whole = new DockPanel();

        if (lying)
        {
            DockPanel.SetDock(Head, Dock.Left);
            DockPanel.SetDock(tabs, Dock.Left);
        }
        else
        {
            DockPanel.SetDock(Head, Dock.Top);
            DockPanel.SetDock(tabs, Dock.Top);
        }

        whole.Children.Add(Head);
        whole.Children.Add(tabs);
        whole.Children.Add(column);

        Child = whole;
    }

    private DockPanel SearchBar()
    {
        Detach(_search, _clear, _sortButton, _smaller, _larger, _saveLayout);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };

        right.Children.Add(_clear);
        right.Children.Add(_sortButton);
        right.Children.Add(_smaller);
        right.Children.Add(_larger);

        if (_open is PanelTab.Layouts)
        {
            right.Children.Add(_saveLayout);
        }

        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(_search);

        return bar;
    }

    /// <summary>The three entrances besides a drop: files chosen, the clipboard, an address (Part 7).</summary>
    private void BuildFooter()
    {
        var files = Small("Add files ...");
        var paste = Small("Paste");
        var fetch = Small("Fetch");

        files.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pictures for the stock",
                Multiselect = true,
                Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.avif;*.tif;*.tiff;*.psd;*.rptok|All files|*.*",
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) is true)
            {
                await _intake.RunAsync(_intake.Entrances.FromFiles(dialog.FileNames), target: null).ConfigureAwait(true);
            }
        };

        paste.Click += async (_, _) => await PasteAsync().ConfigureAwait(true);

        fetch.Click += async (_, _) =>
        {
            var typed = _address.Text?.Trim();

            if (!string.IsNullOrEmpty(typed))
            {
                await _intake.RunAsync(
                    _intake.Entrances.FromDataObject(new DataObject(DataFormats.UnicodeText, typed)), target: null).ConfigureAwait(true);
            }
        };

        _breakOff.Visibility = Visibility.Collapsed;
        _breakOff.Click += (_, _) => _intake.BreakOff();

        _grips.Children.Add(files);
        _grips.Children.Add(paste);
        _grips.Children.Add(_address);
        _grips.Children.Add(fetch);

        _footer.Children.Add(_grips);
        _footer.Children.Add(_breakOff);
    }

    private async Task PasteAsync()
    {
        IDataObject? clipboard;

        try
        {
            clipboard = Clipboard.GetDataObject();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard belongs to whoever last wrote to it and can refuse to be read at any
            // moment. A paste that cannot be read is an empty paste, never a crash.
            return;
        }

        if (clipboard is not null)
        {
            await _intake.RunAsync(_intake.Entrances.FromDataObject(clipboard), target: null).ConfigureAwait(true);
        }
    }

    private static void Detach(params FrameworkElement[] elements)
    {
        foreach (var element in elements)
        {
            switch (element.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(element);
                    break;

                case Decorator decorator:
                    decorator.Child = null;
                    break;

                case ContentControl control:
                    control.Content = null;
                    break;

                default:
                    break;
            }
        }
    }

    private static ToggleButton Tab(string label) =>
        new() { Content = label, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 4, 0) };

    private static Button Small(string label) =>
        new() { Content = label, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), MinWidth = 28 };
}
