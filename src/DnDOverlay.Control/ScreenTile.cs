using System.Windows;
using System.Windows.Controls;
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
/// <b>The row of buttons is deliberately incomplete in M4.</b> The state selector is M5a, the focus
/// M5b, the underlay M5b - four of the eight controls Part 7 draws have no builder here, and a
/// disabled button standing in for them would be a promise rather than an order of work
/// (checks/M4.md). What has a caller is here; the rest arrives with what it does.
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
        Background = Brushes.Transparent;

        var layers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        layers.Children.Add(_images);
        layers.Children.Add(_background);

        var grips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        grips.Children.Add(_unlock);

        _frame = new Border
        {
            Child = _face,
            Margin = new Thickness(0, 4, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gainsboro,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var panel = new StackPanel();

        panel.Children.Add(_head);
        panel.Children.Add(_frame);
        panel.Children.Add(grips);
        panel.Children.Add(layers);

        Child = panel;

        _face.Asked += (_, asked) => Menu(_face, asked.At, asked.Where, asked.Item);
        _face.Carried += (_, carried) => Carried?.Invoke(this, carried);

        // The head is the screen menu's second home, and the reason is a full screen: there is no
        // free tile area left on one, and that is exactly where most is going on (Part 7). A plain
        // tap on the head opens the reasons list instead, which is M5a.
        _head.PreviewTouchDown += (_, down) =>
        {
            var at = down.GetTouchPoint(_head).Position;

            _pressed.Down(at, () => Menu(_head, at, where: null, item: null));
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
        set
        {
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

        _menus.ForScreen(over, at, _scene, _view, _face.Adjusting);
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
