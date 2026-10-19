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

    private readonly Border _frame;

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

        _frame = new Border
        {
            Child = _face,
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

        _menus.ForScreen(over, at, _scene, _view, _face.Adjusting, Opened);
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
