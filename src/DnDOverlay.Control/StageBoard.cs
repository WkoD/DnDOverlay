using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Hub;
using ScreenPoint = System.Windows.Point;

namespace DnDOverlay.Control;

/// <summary>
/// The stage: every screen as a tile, side by side, wrapping by width (Part 7).
/// <para>
/// <b>Wrapping rather than free positions</b>, and that is a decision rather than the easy way: an
/// arrangement laid out on a wide monitor would leave holes and tiles outside the visible area on
/// the surface, and it would need a second layout logic for a gain nobody notices at two to four
/// tiles.
/// </para>
/// <para>
/// <b>The active screen is where the next blind grip lands</b> - the paste hotkey is pressed from
/// inside MapTool, without the control being visible at all, so which tile is active has to be
/// readable at a glance (Part 7). Tapping a tile makes it active.
/// </para>
/// <para>
/// <b>The scenes come from the hub, never from what this window has just sent.</b> A second control
/// changes the same table, and a stage that trusted its own command would drift from it (rule 1).
/// </para>
/// </summary>
internal sealed class StageBoard : Panel
{
    private readonly ISessionApi _session;
    private readonly ControlSettings _settings;
    private readonly Pictures _pictures;
    private readonly Dictionary<ScreenRef, ScreenTile> _tiles = [];
    private readonly Dictionary<ScreenRef, string> _labels = [];

    private IReadOnlyList<ScreenView> _screens = [];
    private ScreenTile? _dragged;
    private ScreenPoint? _from;

    private Carrying? _ghost;
    private (ScreenRef Screen, ItemId Item)? _carried;

    /// <summary>How far a hand travels before this is a drag rather than a press, in DIP.</summary>
    private const double Threshold = 8;

    internal StageBoard(ISessionApi session, ControlSettings settings, Pictures pictures)
    {
        _session = session;
        _settings = settings;
        _pictures = pictures;
    }

    /// <summary>
    /// Whether one screen is open on its own. <b>Switched by a button and never by a swipe</b>: a
    /// swipe on the stage already means moving or panning, and a third meaning for the same grip
    /// would go wrong regularly (Part 7).
    /// </summary>
    internal bool Single
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Lay();
        }
    }

    /// <summary>
    /// Which screen the next grip lands on. <see langword="null"/> only before the first screen is
    /// known - after that the stage always has one, because a blind grip must never have nowhere to
    /// go (Part 7).
    /// </summary>
    internal ScreenRef? Active { get; private set; }

    /// <summary>Raised when the DM has made another tile the active one.</summary>
    internal event EventHandler? ActiveChanged;

    /// <summary>
    /// Raised when the DM asked to set a screen up. The window <i>Devices</i> belongs to the main
    /// window, which owns it and reopens rather than duplicates it.
    /// </summary>
    internal event EventHandler<ScreenRef>? Configuring;

    /// <summary>
    /// What one device is loading, handed to every tile that shows one of its screens.
    /// <para>
    /// Per DEVICE, because that is what the report is about: the same picture may be on its way to
    /// one table and long since standing on another, and two tiles then show two different fills -
    /// which is what Part 7 asks for when it says the weakest wireless must be visible rather than
    /// averaged away.
    /// </para>
    /// </summary>
    internal void Report(DeviceId device, IReadOnlyList<AssetLoad> loads)
    {
        foreach (var (screen, tile) in _tiles)
        {
            if (screen.Device == device)
            {
                tile.Report(loads);
            }
        }
    }

    /// <summary>
    /// The screens as the hub knows them. Tiles come and go with them - a screen that is unplugged
    /// keeps its scene in the hub, but there is nothing to show it on until it is back.
    /// </summary>
    internal void Show(IReadOnlyList<ScreenView> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        _screens = InOrder(screens);

        foreach (var view in _screens)
        {
            _labels[view.Screen] = view.Info.Label;

            if (_tiles.ContainsKey(view.Screen))
            {
                continue;
            }

            var tile = new ScreenTile(view.Screen, _session, _pictures, () => _screens);

            // The menu's three ways out of a tile. Opening one makes it the active screen and
            // opens it, in that order: the open screen IS the active one, in both directions
            // (Part 7).
            tile.Opening += (_, _) =>
            {
                Activate(view.Screen);
                Single = true;
            };

            tile.Configuring += (_, _) => Configuring?.Invoke(this, view.Screen);
            tile.Carried += (_, carried) => Carry(view.Screen, carried);
            tile.Turning += (_, turned) => Turn(view.Screen, turned);

            tile.PreviewMouseDown += (_, _) => Activate(view.Screen);
            tile.PreviewTouchDown += (_, _) => Activate(view.Screen);

            // Dragged by the head, and by nothing else. Mouse and finger are wired separately
            // rather than relying on WPF promoting touch to mouse - that promotion stops the
            // moment manipulation is switched on, which is what the thumbnails get in M4c.
            tile.Handle.PreviewMouseLeftButtonDown += (_, _) => Take(tile);
            tile.Handle.PreviewMouseMove += (_, moved) => Over(tile, moved.GetPosition(this), moved.LeftButton is MouseButtonState.Pressed);
            tile.Handle.PreviewMouseLeftButtonUp += (_, _) => Released();

            tile.Handle.PreviewTouchDown += (_, _) => Take(tile);
            tile.Handle.PreviewTouchMove += (_, moved) => Over(tile, moved.GetTouchPoint(this).Position, dragging: true);
            tile.Handle.PreviewTouchUp += (_, _) => Released();

            _tiles[view.Screen] = tile;
            Children.Add(tile);
        }

        foreach (var gone in _tiles.Keys.Where(screen => !screens.Any(view => view.Screen == screen)).ToList())
        {
            Children.Remove(_tiles[gone]);
            _tiles.Remove(gone);
        }

        if (Active is not { } active || !_tiles.ContainsKey(active))
        {
            Activate(_screens.Count > 0 ? _screens[0].Screen : null);
        }

        Lay();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Two arrangements and one panel. The overview wraps by width - "tiles wrap instead of being
    /// cut off" is what a control on half a surface has to do (Prüfschritt 26) - and the single
    /// view gives the whole room to one tile.
    /// </remarks>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Single)
        {
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(availableSize);
            }

            return new Size(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        }

        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var wanted = child.DesiredSize;

            if (lineWidth + wanted.Width > availableSize.Width && lineWidth > 0)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += wanted.Width;
            lineHeight = Math.Max(lineHeight, wanted.Height);
        }

        return new Size(Math.Max(width, lineWidth), height + lineHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Single)
        {
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new System.Windows.Rect(default, finalSize));
            }

            return finalSize;
        }

        double x = 0, y = 0, lineHeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            var wanted = child.DesiredSize;

            if (x + wanted.Width > finalSize.Width && x > 0)
            {
                x = 0;
                y += lineHeight;
                lineHeight = 0;
            }

            child.Arrange(new System.Windows.Rect(x, y, wanted.Width, wanted.Height));

            x += wanted.Width;
            lineHeight = Math.Max(lineHeight, wanted.Height);
        }

        return finalSize;
    }

    /// <summary>
    /// Which tiles are on show and how large they are. In the single view the others are not
    /// hidden behind the open one - they are not in the tree at all, so nothing of theirs is
    /// measured, arranged or drawn.
    /// </summary>
    private void Lay()
    {
        Children.Clear();

        foreach (var view in _screens)
        {
            var tile = _tiles[view.Screen];

            tile.Opened = Single;

            if (!Single || view.Screen == Active)
            {
                Children.Add(tile);
            }
        }

        InvalidateMeasure();
    }

    /// <summary>
    /// The screens in the order the DM arranged them, with anything new at the end.
    /// <para>
    /// <b>What is not in the saved order is new</b>, and new hangs itself on the end - a screen
    /// pushing into the middle of an arrangement the DM built would be worse than one he has to
    /// place himself. A saved entry with no screen behind it is skipped and KEPT: it is the screen
    /// that is unplugged right now, and it takes its place again when it comes back (Part 7).
    /// </para>
    /// </summary>
    private IReadOnlyList<ScreenView> InOrder(IReadOnlyList<ScreenView> screens)
    {
        var saved = _settings.Current.TileOrder;

        if (saved.Count == 0)
        {
            return screens;
        }

        var places = saved
            .Select((key, index) => (Key: key, Index: index))
            .ToDictionary(entry => entry.Key, entry => entry.Index);

        return
        [
            .. screens.OrderBy(view =>
                places.TryGetValue(Key(view.Screen), out var place) ? place : int.MaxValue),
        ];
    }

    private static ScreenKey Key(ScreenRef screen) => new(screen.Device.Value, screen.Screen.Value);

    /// <summary>
    /// Fetches every scene and hands it to its tile. Called on every patch: what a patch changed is
    /// known to the hub, and asking it is one round trip in the same process - cheaper than a
    /// second copy of the scene state here that could drift from it (rule 1).
    /// </summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        foreach (var view in _screens)
        {
            if (!_tiles.TryGetValue(view.Screen, out var tile))
            {
                continue;
            }

            var scene = await _session.GetSceneAsync(view.Screen, cancellationToken).ConfigureAwait(true);

            // Size and shape are all the drawing takes from the context: where the parked cards
            // lie, how large they are and which way they face is already computed into the items
            // by the hub (Parking.Arrange), so the tile does not need this screen's own parameters
            // to draw them in the right place.
            tile.Show(
                _labels.GetValueOrDefault(view.Screen, view.Info.Label),
                scene,
                ScreenContext.Default(view.Info.Size, view.Info.Dpi),
                View(view.Screen));
        }
    }

    /// <summary>
    /// Turns the view of one screen. <b>It is written where it is read from</b> - in
    /// <c>control.json</c>, per screen - and nothing is sent anywhere: the view rotation is the
    /// control's own property and changes nothing at the table (Part 7). Nor does it take a place
    /// in the undo timeline, for the same reason (Part 4).
    /// </summary>
    private void Turn(ScreenRef screen, ViewRotation view)
    {
        _settings.Update(current => current with
        {
            KnownScreens =
            [
                .. current.KnownScreens.Select(known =>
                    known.DeviceId == screen.Device.Value && known.ScreenId == screen.Screen.Value
                        ? known with { View = view }
                        : known),
            ],
        });

        _ = RefreshAsync();
    }

    /// <summary>
    /// How the DM looks at this screen. Read on every draw rather than kept here: it is written in
    /// one place (Part 7), and a second copy on the stage would be the one that is stale after the
    /// menu has set it.
    /// </summary>
    private ViewRotation View(ScreenRef screen) =>
        _settings.Current.KnownScreens
            .FirstOrDefault(known =>
                known.DeviceId == screen.Device.Value && known.ScreenId == screen.Screen.Value)
            ?.View
        ?? ViewRotation.None;

    /// <summary>
    /// The tile a hand has taken hold of, and where it started. Nothing moves yet: a press on the
    /// head is how the DM reaches the screen menu as well (M4c), so the drag begins only once the
    /// hand has actually travelled.
    /// </summary>
    private void Take(ScreenTile tile)
    {
        _dragged = tile;
        _from = null;
    }

    /// <summary>
    /// The hand has moved. Once it is past the threshold the tile moves between the others - the
    /// order changes while the DM is looking at it, which is what "the others make way" means
    /// (Prüfschritt 24b); the arrangement is written when the hand lets go.
    /// </summary>
    private void Over(ScreenTile tile, ScreenPoint at, bool dragging)
    {
        if (!dragging || _dragged != tile)
        {
            return;
        }

        _from ??= at;

        if (Math.Abs(at.X - _from.Value.X) + Math.Abs(at.Y - _from.Value.Y) < Threshold)
        {
            return;
        }

        if (Under(at) is not { } target || target == tile)
        {
            return;
        }

        var order = _screens.Select(view => view.Screen).ToList();
        var taken = order.IndexOf(tile.Screen);
        var place = order.IndexOf(target.Screen);

        if (taken < 0 || place < 0)
        {
            return;
        }

        order.RemoveAt(taken);
        order.Insert(place, tile.Screen);

        _screens = [.. order.Select(screen => _screens.First(view => view.Screen == screen))];

        Children.Clear();

        foreach (var screen in order)
        {
            Children.Add(_tiles[screen]);
        }
    }

    /// <summary>
    /// The hand let go. The arrangement is written now rather than on every step of the drag: the
    /// configuration file debounces, and twenty writes on the way to a place the DM has not chosen
    /// yet would be twenty answers to a question he is still asking.
    /// </summary>
    private void Released()
    {
        if (_dragged is null)
        {
            return;
        }

        _dragged = null;
        _from = null;

        var order = _screens.Select(view => Key(view.Screen)).ToList();

        // What is kept is the screens that are here PLUS the saved places of the ones that are
        // not: a screen that is unplugged during a rearrangement must not lose its place (Part 7).
        var absent = _settings.Current.TileOrder.Where(key => !order.Contains(key));

        _settings.Update(current => current with { TileOrder = [.. order, .. absent] });
    }

    /// <summary>
    /// A picture on its way from one tile to another. <b>The stage does this rather than either
    /// tile</b>: the source knows only that the hand has left it, and which tile the hand is over
    /// is a question only something that sees all of them can answer (Part 7).
    /// </summary>
    private void Carry(ScreenRef source, TileFace.Carry carried)
    {
        var here = PointFromScreen(carried.At);

        switch (carried.Phase)
        {
            case TileFace.Phase.Began:
                _carried = (source, carried.Item);
                _ghost = new Carrying(this, carried.Look);

                AdornerLayer.GetAdornerLayer(this)?.Add(_ghost);
                _ghost.At(here);

                break;

            case TileFace.Phase.Moved:
                _ghost?.At(here);

                break;

            case TileFace.Phase.Dropped:
                Land(carried);

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Where the picture came down. <b>The tile decides first, then the place within it</b> - and
    /// the place is read through the TARGET tile's own view rotation, or a picture carried onto a
    /// table the DM looks at from the side would land where the source tile would have put it
    /// (Part 7, Part 10).
    /// <para>
    /// <b>Copying is the same thing with a modifier held</b> (Part 7). Dropped on the tile it came
    /// from, or on nothing at all, the carry simply ends: the picture already lies where the hand
    /// last had it inside its own tile, and a move onto the screen it is already on does nothing by
    /// design (Part 4).
    /// </para>
    /// </summary>
    private void Land(TileFace.Carry carried)
    {
        if (_ghost is { } ghost)
        {
            AdornerLayer.GetAdornerLayer(this)?.Remove(ghost);
        }

        _ghost = null;

        var taken = _carried;

        _carried = null;

        if (taken is not { } from
            || _tiles.Values.FirstOrDefault(tile => tile.Landing(carried.At) is not null) is not { } target
            || target.Screen == from.Screen)
        {
            return;
        }

        var place = target.Landing(carried.At);

        _ = carried.Copy
            ? _session.CopyItemAsync(from.Screen, target.Screen, from.Item, place, CancellationToken.None)
            : _session.MoveItemAsync(from.Screen, target.Screen, from.Item, place, CancellationToken.None);

        // The DM has just worked on the target, so that is where the next blind grip lands
        // (Part 7).
        Activate(target.Screen);
    }

    private ScreenTile? Under(ScreenPoint at) =>
        _tiles.Values.FirstOrDefault(tile =>
            tile.IsVisible
            && new System.Windows.Rect(tile.TranslatePoint(default, this), tile.RenderSize).Contains(at));

    private void Activate(ScreenRef? screen)
    {
        if (Active == screen)
        {
            return;
        }

        Active = screen;

        foreach (var (reference, tile) in _tiles)
        {
            tile.Active = reference == screen;
        }

        // The open screen IS the active one, in both directions (Part 7): opening one makes it
        // active, and making another one active while a screen is open shows that one instead.
        if (Single)
        {
            Lay();
        }

        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
