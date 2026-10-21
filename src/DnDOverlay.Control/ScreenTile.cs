using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Hub;
using CorePoint = DnDOverlay.Core.Point;
using TilePoint = System.Windows.Point;

namespace DnDOverlay.Control;

/// <summary>
/// One screen on the stage: a head of exactly one line, the thumbnail, and the row of buttons -
/// three areas of fixed height, so the tile is the same size whether it has twelve reasons or none
/// (Part 7).
/// <para>
/// <b>Two channels, two statements, never mixed.</b> A FRAME around the tile means "this is where
/// the next grip lands" and nothing else; the colour of the head means "this is how the screen
/// stands" and arrives in M5a with the state selector. Nothing else on the stage is framed or
/// coloured.
/// </para>
/// <para>
/// <b>The row of buttons is still incomplete.</b> The state selector came with M5a; the focus and
/// the underlay are M5b, and a disabled button standing in for them would be a promise rather than
/// an order of work (checks/M4.md). What has a caller is here; the rest arrives with what it does.
/// </para>
/// </summary>
internal sealed class ScreenTile : Border
{
    private readonly ISessionApi _session;
    private readonly TileHead _head = new();
    private readonly TileFace _face;
    private readonly TileMenus _menus;

    /// <summary>
    /// The long press on the head. <b>Its own clock beside the face's</b>, because a head and a
    /// face are two surfaces a finger can be on - but the rule they run is the one rule
    /// (<see cref="Press"/>), and movement cancels it on both, which is what lets the head carry
    /// the arrangement drag and the screen menu at once (Prüfschritt 24b).
    /// </summary>
    private readonly Press _pressed = new();

    private SceneState _scene = SceneState.Empty;
    private ScreenContext _context = ScreenContext.Default(new PixelSize(1920, 1080), 96);
    private ViewRotation _view;
    private readonly CheckBox _images = new() { Content = "Images", Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox _background = new() { Content = "Background", Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button _unlock = new() { Content = "Unlock all", Padding = new Thickness(8, 2, 8, 2) };

    /// <summary>
    /// The state selector (Part 7): the WISH, permanently labelled - not what gets in the way, which
    /// stands in the head. Without the standing label "frozen" would be the one state only found by
    /// trying, because it covers nothing and colours no head.
    /// </summary>
    private readonly ComboBox _state = new()
    {
        MinWidth = 120,
        Margin = new Thickness(0, 0, 8, 0),
        ItemsSource = States,
    };

    /// <summary>The reasons, laid over the thumbnail when the head is tapped (Part 7).</summary>
    private readonly Popup _reasons;
    private readonly StackPanel _reasonLines = new();
    private IReadOnlyList<ScreenReason> _why = [];

    private TilePoint? _tapFrom;
    private bool _menuRang;

    /// <summary>
    /// The diagnostic view (Part 7): it takes the thumbnail's place with this screen's connection
    /// data and the device's reading, for as long as it stands.
    /// </summary>
    private readonly TextBlock _diagnosis = new()
    {
        Margin = new Thickness(8),
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Consolas"),
    };

    private readonly Border _diagnosisPane;
    private DeviceView? _device;
    private DiagnosticsReading? _reading;

    /// <summary>The size the thumbnail had in the overview, which the pane keeps there.</summary>
    private Size _paneSize;

    private readonly Border _frame;

    /// <summary>
    /// The thumbnail with the background strip over its lower edge - the target for "as background"
    /// while something is carried out of the panel (Part 7). <b>Not the head</b>: that is the grip
    /// to drag by and the place for the menu, and must not get a third meaning.
    /// </summary>
    private readonly Grid _faceHost = new();

    private readonly Border _strip = new()
    {
        VerticalAlignment = VerticalAlignment.Bottom,
        Background = new SolidColorBrush(Color.FromArgb(0x90, 0x46, 0x82, 0xB4)),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
        Child = new TextBlock
        {
            Text = "Background",
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private bool _setting;

    internal ScreenTile(
        ScreenRef screen,
        ISessionApi session,
        Pictures pictures,
        Func<IReadOnlyList<ScreenView>> targets)
    {
        Screen = screen;
        _session = session;
        _face = new TileFace(screen, session, pictures, Selected);
        _menus = new TileMenus(screen, session, targets);

        BorderThickness = new Thickness(2);
        BorderBrush = Brushes.Transparent;
        Padding = new Thickness(6);
        Margin = new Thickness(0, 0, 8, 8);
        // The whole tile answers a grip, not only what is drawn on it: the padding round the
        // face and the strip beside the buttons carry the screen menu too (hand-run of M4, 25v).
        Background = Brushes.Transparent;

        var layers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        layers.Children.Add(_images);
        layers.Children.Add(_background);

        var grips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        grips.Children.Add(_state);
        grips.Children.Add(_unlock);

        _faceHost.Children.Add(_face);
        _faceHost.Children.Add(_strip);

        _frame = new Border
        {
            Child = _faceHost,
            Margin = new Thickness(0, 4, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gainsboro,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // <b>Ein Raster, und die Miniatur bekommt die Sternzeile.</b> Ein Stapel bietet jedem
        // Kind unendliche Höhe an, und die offene Kachel fällt dann auf ihre kleine zurück - genau
        // das, was am Tisch zweimal gemeldet wurde: erst blieb sie null Pixel hoch, dann blieb sie
        // so hoch wie in der Übersicht, egal wie groß das Fenster wurde. Derselbe Fehler eine Ebene
        // tiefer als beim Fenster (Handlauf M4, dritter Lauf, 24a).
        //
        // In der Übersicht ändert es nichts: Dort bekommt die Kachel ihre Wunschgröße, und eine
        // Sternzeile in einem Behälter, der sich nach dem Inhalt richtet, ist so groß wie ihr Inhalt.
        var panel = new Grid();

        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_head, 0);
        Grid.SetRow(_frame, 1);
        Grid.SetRow(grips, 2);
        Grid.SetRow(layers, 3);

        panel.Children.Add(_head);
        panel.Children.Add(_frame);
        panel.Children.Add(grips);
        panel.Children.Add(layers);

        Child = panel;

        _diagnosisPane = new Border { Child = _diagnosis, Background = Brushes.WhiteSmoke };

        // A drop from outside - Explorer, a browser, the editor's preview - onto this tile: taken into
        // the stock and put on THIS screen where it was let go (Part 7: ingest, entry, AddItem).
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            e.Handled = true;
            DroppedFromOutside?.Invoke(this, new Outside(e.Data, _face.Landing(PointToScreen(e.GetPosition(this)))));
        };

        // Over the thumbnail, pushing nothing away: the DM opens it himself and the next tap closes
        // it, and moving the thumbnail would break the grid in the overview (Part 7).
        _reasons = new Popup
        {
            PlacementTarget = _frame,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
            AllowsTransparency = true,
            StaysOpen = true,
            Child = new Border
            {
                Child = _reasonLines,
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
            },
        };

        _state.SelectionChanged += async (_, _) => await ChooseAsync().ConfigureAwait(true);

        // A tap on the head opens the reasons, a long press the screen menu, a movement the drag -
        // three grips on one strip, told apart by the one tolerance Press holds (Part 7). The whole
        // line answers, not only the arrow: on the Surface the arrow alone is too small.
        _head.PreviewMouseLeftButtonDown += (_, down) =>
        {
            if (down.StylusDevice is null)
            {
                _tapFrom = down.GetPosition(_head);
            }
        };

        _head.PreviewMouseLeftButtonUp += (_, up) =>
        {
            if (up.StylusDevice is null)
            {
                Tapped(up.GetPosition(_head));
            }
        };

        _head.PreviewTouchDown += (_, down) =>
        {
            _tapFrom = down.GetTouchPoint(_head).Position;
            _menuRang = false;
        };

        _head.PreviewTouchUp += (_, up) => Tapped(up.GetTouchPoint(_head).Position);

        _face.Asked += (_, asked) => Menu(_face, asked.At, asked.Where, asked.Item);
        _face.Carried += (_, carried) => Carried?.Invoke(this, carried);

        // The head is the screen menu's second home, and the reason is a full screen: there is no
        // free tile area left on one, and that is exactly where most is going on (Part 7). A plain
        // tap on the head opens the reasons list instead, which is M5a.
        _head.PreviewTouchDown += (_, down) =>
        {
            var at = down.GetTouchPoint(_head).Position;

            _pressed.Down(at, () =>
            {
                _menuRang = true;
                Menu(_head, at, where: null, item: null);
            });
        };

        _head.PreviewTouchMove += (_, over) => _pressed.Moved(over.GetTouchPoint(_head).Position);
        _head.PreviewTouchUp += (_, _) => _pressed.Up();

        _head.PreviewMouseRightButtonUp += (_, clicked) =>
        {
            Menu(_head, clicked.GetPosition(_head), where: null, item: null);
            clicked.Handled = true;
        };

        _menus.Opening += (_, _) => Opening?.Invoke(this, EventArgs.Empty);
        _menus.Configuring += (_, _) => Configuring?.Invoke(this, EventArgs.Empty);
        _menus.Turning += (_, view) => Turning?.Invoke(this, view);
        _menus.Adjusting += (_, adjusting) => _face.Adjusting = adjusting;
        _menus.Diagnosing += (_, on) => Diagnose(on);
        _menus.Saving += (_, _) => Saving?.Invoke(this, EventArgs.Empty);
        _menus.ShowingInStock += (_, asset) => ShowingInStock?.Invoke(this, asset);
        _menus.Renaming += (_, asset) => Renaming?.Invoke(this, asset);

        _images.Click += async (_, _) => await ToggleAsync(images: true).ConfigureAwait(true);
        _background.Click += async (_, _) => await ToggleAsync(images: false).ConfigureAwait(true);
        _unlock.Click += async (_, _) =>
            await _session.UnlockAllAsync(Screen, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Which screen this tile is. It is the address of everything the tile does.</summary>
    internal ScreenRef Screen { get; }

    /// <summary>
    /// What is picked out on this screen. <b>One per screen and not one for the stage</b>: a
    /// selection across screens does not exist in the model, because <c>FocusItems</c> belongs to a
    /// scene (Part 3), and the focus button of M5b would not know what it referred to.
    /// </summary>
    internal Selection Selected { get; } = new();

    /// <summary>A picture has left this tile in somebody's hand.</summary>
    internal event EventHandler<TileFace.Carry>? Carried;

    /// <summary>The DM asked for this screen on its own.</summary>
    internal event EventHandler? Opening;

    /// <summary>The DM asked for the window <i>Devices</i> with this screen to hand.</summary>
    internal event EventHandler? Configuring;

    /// <summary>The DM turned the view of this screen.</summary>
    internal event EventHandler<ViewRotation>? Turning;

    /// <summary>The DM asked to save this screen as a scene.</summary>
    internal event EventHandler? Saving;

    /// <summary>The item menu asked to show a picture in the stock.</summary>
    internal event EventHandler<AssetId>? ShowingInStock;

    /// <summary>The item menu asked to rename a picture.</summary>
    internal event EventHandler<AssetId>? Renaming;

    /// <summary>
    /// Shows or hides the background strip - shown on every tile for as long as something is carried
    /// out of the panel, so the target is there before the hand gets to it. A fifth of the thumbnail
    /// and never less than a finger's width.
    /// </summary>
    internal void Strip(bool showing)
    {
        _strip.Height = Math.Max(26, _faceHost.ActualHeight / 5);
        _strip.Visibility = showing && !Diagnosed ? Visibility.Visible : Visibility.Collapsed;
        Lit(false);
    }

    /// <summary>Whether a place on the screen lies on the background strip - and lights it while it does.</summary>
    internal bool OnStrip(TilePoint absolute)
    {
        if (_strip.Visibility is not Visibility.Visible || !_strip.IsLoaded)
        {
            return false;
        }

        var local = _strip.PointFromScreen(absolute);
        var on = local.X >= 0 && local.Y >= 0 && local.X <= _strip.ActualWidth && local.Y <= _strip.ActualHeight;

        Lit(on);

        return on;
    }

    /// <summary>Takes the light off the strip - the hand is over another tile, or over none.</summary>
    internal void Dim() => Lit(false);

    private void Lit(bool on) =>
        _strip.Background = new SolidColorBrush(on ? Color.FromArgb(0xE0, 0x46, 0x82, 0xB4) : Color.FromArgb(0x90, 0x46, 0x82, 0xB4));

    /// <summary>Something was dropped onto this tile from outside the control.</summary>
    internal event EventHandler<Outside>? DroppedFromOutside;

    /// <summary>What was dropped, and where on the screen it landed - or nowhere precise when it missed the face.</summary>
    internal sealed record Outside(IDataObject Data, CorePoint? At);

    /// <summary>The diagnostic view of this tile went on or off - the board asks the device for its readings.</summary>
    internal event EventHandler<bool>? Diagnosing;

    /// <summary>Whether this tile shows its diagnostic view instead of the thumbnail.</summary>
    internal bool Diagnosed { get; private set; }

    /// <summary>
    /// The head, and the one place a tile may be dragged by (Part 7). The tile's face is taken -
    /// items lie on it and the selection frame is drawn from it - while the head is the one strip
    /// that exists on every tile and is always the same size.
    /// </summary>
    internal UIElement Handle => _head;

    /// <summary>
    /// Whether this tile is the one open on its own - then the thumbnail takes the room it is
    /// given instead of its own height, and the head and the buttons stay exactly as they are
    /// (Part 7: "one tile filling the format, with the same head and the same buttons").
    /// </summary>
    internal bool Opened
    {
        get;
        set
        {
            field = value;

            // Not remembered: a change of view closes the list (Part 7).
            CloseReasons();

            if (Diagnosed)
            {
                _diagnosisPane.Width = value ? double.NaN : _paneSize.Width;
                _diagnosisPane.Height = value ? double.NaN : _paneSize.Height;
            }
            _face.Opened = value;
            _frame.VerticalAlignment = value ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            _frame.HorizontalAlignment = value ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        }
    }

    /// <summary>
    /// Whether this is the screen the next blind grip lands on - the paste hotkey, the double tap
    /// in the stock, the pre-selected entry in every target list (Part 7).
    /// </summary>
    internal bool Active
    {
        set => BorderBrush = value ? Brushes.SteelBlue : Brushes.Transparent;
    }

    /// <summary>
    /// What this tile shows from now on. The scene is drawn at the next render pass rather than
    /// here, so twenty arriving patches cost one drawing (<see cref="Redraw"/>).
    /// </summary>
    internal void Show(string label, SceneState scene, ScreenContext screen, ViewRotation view)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        _scene = scene;
        _context = screen;
        _view = view;

        _head.Show(label, screen.Size.Width, screen.Size.Height);
        _face.Show(scene, screen, view);

        // Set without sending: these are switches that carry their own state (Part 7), and a
        // checkbox that fires its own click handler on being told the truth would send the DM's
        // last command back at him inverted.
        _setting = true;
        _images.IsChecked = scene.ItemsVisible;
        _background.IsChecked = scene.BackgroundVisible;
        _setting = false;
    }

    /// <summary>
    /// What the hub knows about this screen and its device beyond the scene: the wish for the
    /// selector, the reasons for the mark, the battery for the head. Asked on every device event, so
    /// a battery that moves or a device that leaves shows at once.
    /// </summary>
    /// <param name="device">The screen's device, or <see langword="null"/> when it is not paired.</param>
    internal void Know(ScreenView view, DeviceView? device)
    {
        ArgumentNullException.ThrowIfNull(view);

        _setting = true;
        _state.SelectedItem = States.FirstOrDefault(choice => choice.State == view.State);
        _setting = false;

        _head.Inactive = view.State == ScreenState.Inactive;
        _head.Battery(device is { Connected: true } ? device.Battery : null);

        _device = device;

        if (Diagnosed)
        {
            Describe();
        }

        var why = ScreenReasons.Of(view, device);

        if (!why.SequenceEqual(_why))
        {
            _why = why;
            _head.Reasons(ScreenReasons.Mark(why));

            if (why.Count == 0)
            {
                CloseReasons();
            }
            else if (_reasons.IsOpen)
            {
                FillReasons();
            }
        }
    }

    /// <summary>
    /// What the device of this screen is loading. Straight through to the layer that draws it -
    /// nothing is bundled on the way, which is the whole reason that layer exists.
    /// </summary>
    internal void Report(IReadOnlyList<AssetLoad> loads) => _face.Report(loads);

    /// <summary>Where fingers are lying on this screen.</summary>
    internal void Touching(IReadOnlyList<TouchTrail> touches) => _face.Touching(touches);

    /// <summary>
    /// Where a place on the screen lands on this table - the target half of the hit test across
    /// tile borders, through this tile's own view rotation.
    /// </summary>
    internal CorePoint? Landing(TilePoint absolute) => _face.Landing(absolute);

    /// <inheritdoc cref="TileFace.SelectWhenItArrives"/>
    internal void SelectWhenItArrives(ItemId item) => _face.SelectWhenItArrives(item);

    /// <summary>
    /// Which of the two menus this grip asks for: the picture's if one was hit, the screen's
    /// otherwise. <b>That is not a grip with two meanings but the nature of a context menu</b> - the
    /// action is always the same one (Part 7).
    /// </summary>
    private void Menu(UIElement over, TilePoint at, CorePoint? where, ItemId? item)
    {
        if (item is { } hit
            && where is { } place
            && _scene.Items.FirstOrDefault(one => one.ItemId == hit) is { } picture)
        {
            _menus.ForItem(over, at, _scene, picture, _context, Selected, place);

            return;
        }

        _menus.ForScreen(over, at, _scene, _view, _face.Adjusting, Opened, Diagnosed, _context.Placement);
    }

    /// <summary>A reading from this screen's device, for the diagnostic view.</summary>
    internal void Reading(DiagnosticsReading reading)
    {
        _reading = reading;

        if (Diagnosed)
        {
            Describe();
        }
    }

    /// <summary>
    /// Puts the diagnostic view in the thumbnail's place, or the thumbnail back. The tile keeps its
    /// size either way - the pane takes exactly the room the thumbnail had - so the grid of the
    /// overview does not move (Part 7).
    /// </summary>
    private void Diagnose(bool on)
    {
        if (on == Diagnosed)
        {
            return;
        }

        Diagnosed = on;
        CloseReasons();

        if (on)
        {
            _reading = null;
            _paneSize = new Size(_face.ActualWidth, _face.ActualHeight);
            _diagnosisPane.Width = Opened ? double.NaN : _paneSize.Width;
            _diagnosisPane.Height = Opened ? double.NaN : _paneSize.Height;
            _frame.Child = _diagnosisPane;
            Describe();
        }
        else
        {
            _frame.Child = _faceHost;
        }

        Diagnosing?.Invoke(this, on);
    }

    /// <summary>
    /// What the view says: the same numbers as the bar at the device, from the same reading, plus
    /// what only the control has - the round trip it measured, the device's address and version.
    /// </summary>
    private void Describe()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var lines = new List<string>();

        if (_device is not { Connected: true } device)
        {
            _diagnosis.Text = "Device not connected.";
            return;
        }

        lines.Add(string.Format(
            culture,
            "{0} · {1} · version {2} · {3}",
            device.Name,
            device.Address ?? "?",
            device.AppVersion ?? "?",
            device.RoundTrip is { } trip ? string.Create(culture, $"{trip.TotalMilliseconds:0} ms") : "? ms"));

        if (_reading is not { } reading)
        {
            lines.Add("Waiting for the first reading ...");
        }
        else
        {
            lines.Add(reading.MedianMs is { } median
                ? string.Format(culture, "Frames {0:0.0} / {1:0.0} / {2:0.0} ms ({3}) · {4} stalls", median, reading.P95Ms, reading.MaxMs, reading.Frames, reading.Stalls)
                : "Frames: too few to say");
            lines.Add(string.Format(culture, "CPU {0:0.0} % · loading {1}, waiting {2}", reading.CpuPercent, reading.Downloading, reading.Waiting));
            lines.Add(string.Format(
                culture,
                "Store {0} pictures, {1} MB · memory {2} / {3} MB",
                reading.CachedPictures,
                reading.CacheBytes / (1024 * 1024),
                reading.MemoryBytes / (1024 * 1024),
                reading.MemoryCapBytes / (1024 * 1024)));
        }

        _diagnosis.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>The four states in the selector's words - the enum's names are for files, not for people.</summary>
    private static readonly IReadOnlyList<StateChoice> States =
    [
        new(ScreenState.Enabled, "Active"),
        new(ScreenState.Disabled, "Frozen"),
        new(ScreenState.Diagnostic, "Diagnostic"),
        new(ScreenState.Inactive, "Inactive"),
    ];

    private sealed record StateChoice(ScreenState State, string Label)
    {
        public override string ToString() => Label;
    }

    private async Task ChooseAsync()
    {
        if (_setting || _state.SelectedItem is not StateChoice choice)
        {
            return;
        }

        await _session.SetScreenStateAsync(Screen, choice.State, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// A press on the head that came up where it went down and did not ring the menu clock is a
    /// tap, and a tap toggles the reasons - with none, there is nothing to open.
    /// </summary>
    private void Tapped(TilePoint at)
    {
        var from = _tapFrom;

        _tapFrom = null;

        if (from is not { } start
            || _menuRang
            || Math.Abs(at.X - start.X) + Math.Abs(at.Y - start.Y) > Press.Tolerance)
        {
            _menuRang = false;
            return;
        }

        if (_reasons.IsOpen)
        {
            CloseReasons();
        }
        else if (_why.Count > 0)
        {
            FillReasons();
            _reasons.Width = Math.Max(160, _frame.ActualWidth);
            _reasons.IsOpen = true;

            // A tap anywhere else closes it again. Watched on the window, because the list lies in
            // a popup of its own and the tile does not see a tap on its neighbour.
            if (Window.GetWindow(this) is { } window)
            {
                window.PreviewMouseDown += Elsewhere;
                window.PreviewTouchDown += Elsewhere;
                _watching = window;
            }
        }
    }

    private Window? _watching;

    private void Elsewhere(object? sender, System.Windows.Input.InputEventArgs e)
    {
        // The head toggles by itself; anywhere else closes.
        if (e.OriginalSource is DependencyObject hit && IsWithin(hit, _head))
        {
            return;
        }

        CloseReasons();
    }

    private static bool IsWithin(DependencyObject hit, DependencyObject container)
    {
        for (var at = hit; at is not null; at = VisualTreeHelper.GetParent(at) ?? LogicalTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, container))
            {
                return true;
            }
        }

        return false;
    }

    private void CloseReasons()
    {
        if (_reasons is not null)
        {
            _reasons.IsOpen = false;
        }

        if (_watching is { } window)
        {
            window.PreviewMouseDown -= Elsewhere;
            window.PreviewTouchDown -= Elsewhere;
            _watching = null;
        }
    }

    /// <summary>One reason a line, in the order of the rule - and with one it is the same list of one line.</summary>
    private void FillReasons()
    {
        _reasonLines.Children.Clear();

        foreach (var reason in _why)
        {
            _reasonLines.Children.Add(new TextBlock
            {
                Text = ScreenReasons.Words(reason),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2),
            });
        }
    }

    private async Task ToggleAsync(bool images)
    {
        if (_setting)
        {
            return;
        }

        var wanted = (images ? _images.IsChecked : _background.IsChecked) ?? true;

        if (images)
        {
            await _session.ToggleItemsAsync(Screen, wanted, CancellationToken.None).ConfigureAwait(true);
        }
        else
        {
            await _session.ToggleBackgroundAsync(Screen, wanted, CancellationToken.None).ConfigureAwait(true);
        }
    }
}
