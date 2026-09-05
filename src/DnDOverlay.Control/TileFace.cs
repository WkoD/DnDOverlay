using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Hub;
using CoreManipulation = DnDOverlay.Core.Manipulation;
using CorePoint = DnDOverlay.Core.Point;
using CoreRect = DnDOverlay.Core.Rect;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// The face of a tile: the three layers that show one screen, laid one exactly over the other, and
/// every grip that lands on the scene.
/// <para>
/// <b>The layers are a panel of its own because they must share one rectangle.</b> They are three
/// elements rather than one drawing on purpose - the scene is bundled to one pass, the loading fill
/// is not, and the marks belong to the control rather than to the scene (Part 7, rank 3 before 4) -
/// but a mark half a pixel off its picture is a mark on the wrong picture. Left to a <c>Grid</c>
/// they would each take the whole slot and the drawing would stretch out of shape in the single
/// view, where the tile is wider than the table.
/// </para>
/// <para>
/// <b>The face keeps the table's shape</b>, turned the way the DM looks at it
/// (<see cref="Viewing.AspectRatioInView"/>): a table seen from its short side is upright in the
/// tile, and a face that stayed landscape would stretch everything on it.
/// </para>
/// <para>
/// <b>The gesture arithmetic is the table's own</b> (<see cref="CoreManipulation"/>): the dead
/// zone, the snap on release, the edge clamp and "turn to me" are the same functions with the same
/// parameters. Prüfschritt 22 signs the milestone off on that - "the same three grips in the
/// thumbnail, identical behaviour, no relearning" - and a second set of rules here would be two
/// feels rather than one (rule 9).
/// </para>
/// <para>
/// <b>What is NOT the table's own is what suppresses a gesture.</b> At the table three things do
/// (<c>AcceptsGestures</c>); here none of them does. The padlock guards against the table and not
/// against the DM (Part 3), a frozen or blacked-out screen is exactly the one being prepared
/// (Part 7, Prüfschritt 37e), and a focus is something the DM sets rather than something that stops
/// him.
/// </para>
/// </summary>
internal sealed class TileFace : Panel
{
    private readonly ScreenRef _screenRef;
    private readonly ISessionApi _session;
    private readonly SceneThumbnail _thumbnail;
    private readonly Loading _loading;
    private readonly Marks _marks;
    private readonly Selection _selection;

    /// <summary>
    /// The same limit the table reports under, for a related reason: every report becomes a patch,
    /// and every patch is broadcast to every display. Sixty a second from a hand in the thumbnail
    /// would be the load Part 7 warns about, produced by the surface that has to stay readable
    /// under it.
    /// </summary>
    private readonly TransformThrottle _throttle = new();

    private readonly Tapping _tapping = new();

    /// <summary>
    /// The long press, and the rule that keeps it from strangling a drag. <b>One instance per
    /// surface, one rule for all of them</b> (Part 7) - the head has its own beside this.
    /// </summary>
    private readonly Press _press = new();

    private SceneState _scene = SceneState.Empty;
    private ScreenContext _screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
    private ViewRotation _view;

    private TilePoint? _pressed;
    private TilePoint _mouseAt;
    private TilePoint? _framing;
    private Hold? _hold;
    private bool _spent;

    /// <summary>How tall a face is in the overview, in DIP. The width follows the table's shape.</summary>
    internal const double Small = 150;

    internal TileFace(ScreenRef screen, ISessionApi session, Pictures pictures, Selection selection)
    {
        _screenRef = screen;
        _session = session;
        _selection = selection;
        _thumbnail = new SceneThumbnail(pictures);
        _loading = new Loading(pictures);
        _marks = new Marks(selection);

        Children.Add(_thumbnail);
        Children.Add(_loading);
        Children.Add(_marks);

        ClipToBounds = true;
        IsManipulationEnabled = true;

        // Mouse and finger are wired apart rather than left to WPF's promotion of touch to mouse:
        // that promotion stops the moment manipulation is switched on, and it is switched on here.
        PreviewMouseLeftButtonDown += (_, pressed) => Pressed(pressed);
        PreviewMouseMove += (_, moved) => Dragged(moved);
        PreviewMouseLeftButtonUp += (_, released) => Lifted(released);
        PreviewMouseWheel += (_, turned) => Wheel(turned);

        ManipulationStarting += (_, starting) =>
        {
            starting.ManipulationContainer = this;
            starting.Mode = ManipulationModes.All;
        };

        // The clock for the long press runs on the touch events rather than on the manipulation:
        // it has to start the moment the finger lands, before anything has been taken hold of.
        PreviewTouchDown += (_, down) => Held(down.GetTouchPoint(this).Position);
        PreviewTouchMove += (_, over) => _press.Moved(over.GetTouchPoint(this).Position);
        PreviewTouchUp += (_, _) => _press.Up();

        // A mouse asks for a menu with its right button, and never by holding the left one: what
        // "holding" means must not differ between a finger and a mouse (Part 7).
        PreviewMouseRightButtonUp += (_, clicked) =>
        {
            Menu(clicked.GetPosition(this));
            clicked.Handled = true;
        };

        ManipulationStarted += (_, started) => Started(started.ManipulationOrigin);
        ManipulationDelta += (_, moved) => Delta(moved);
        ManipulationCompleted += (_, done) => Completed(done);
    }

    /// <summary>Raised when a grip on this face has changed what is selected on this screen.</summary>
    internal event EventHandler? Touched;

    /// <summary>
    /// A menu was asked for: on a picture, or on free tile area. What the two contain is the tile's
    /// business, not the face's - the face knows where the hand was and what lies there.
    /// </summary>
    internal event EventHandler<MenuAsk>? Asked;

    /// <summary>
    /// Whether this face has the whole room of an open tile rather than its own small height.
    /// </summary>
    internal bool Opened
    {
        get;
        set
        {
            field = value;
            InvalidateMeasure();
        }
    }

    /// <summary>What this face shows from now on.</summary>
    internal void Show(SceneState scene, ScreenContext screen, ViewRotation view)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var before = Shape();

        _scene = scene;
        _screen = screen;
        _view = view;

        // A picture that has left this screen is no longer selected - a menu command to an item
        // that is not there would be ineffective at the hub and a broken promise in the surface.
        _selection.Keep(scene);

        Draw();

        // Only when the table itself changed shape - a screen re-plugged at another resolution, or
        // the view turned. Every arriving patch asking for a new measure would put the whole stage
        // through a layout pass sixty times a second, which is what the bundling is there to avoid.
        if (before != Shape())
        {
            InvalidateMeasure();
        }
    }

    /// <summary>What the device of this screen is loading. Straight through, ungoverned by the bundling.</summary>
    internal void Report(IReadOnlyList<AssetLoad> loads) => _loading.Report(loads);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var wanted = Wanted(availableSize);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(wanted);
        }

        return wanted;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Centred, so an open tile that is wider than the table has its margin on both sides. The
        // layers all get the SAME rectangle, which is the whole reason this panel exists.
        var face = Wanted(finalSize);
        var place = new TileRect(
            Math.Max(0, (finalSize.Width - face.Width) / 2),
            Math.Max(0, (finalSize.Height - face.Height) / 2),
            face.Width,
            face.Height);

        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(place);
        }

        return finalSize;
    }

    /// <summary>
    /// The largest rectangle of the table's shape that fits in what is offered.
    /// <para>
    /// In the overview the height is fixed and leads: the tiles are rows of a wrapping arrangement,
    /// and rows of unequal height leave holes in it (Part 7).
    /// </para>
    /// </summary>
    private Size Wanted(Size available)
    {
        var shape = Shape();

        if (shape <= 0)
        {
            return new Size(0, 0);
        }

        var height = Opened
            ? double.IsInfinity(available.Height) ? Small : available.Height
            : Small;

        var width = height * shape;

        return double.IsInfinity(available.Width) || width <= available.Width
            ? new Size(width, height)
            : new Size(available.Width, available.Width / shape);
    }

    /// <summary>
    /// The shape the table has as the DM sees it - <b>asked of the drawing rather than worked out
    /// here</b>. Two answers to that question would put the marks on a rectangle the picture is not
    /// on (rule 9).
    /// </summary>
    private double Shape() => _thumbnail.AspectRatio;

    /// <summary>Where the face itself lies inside this panel - what a grip has to subtract.</summary>
    private TilePoint OnFace(TilePoint at)
    {
        var face = Wanted(RenderSize);

        return new TilePoint(
            at.X - Math.Max(0, (RenderSize.Width - face.Width) / 2),
            at.Y - Math.Max(0, (RenderSize.Height - face.Height) / 2));
    }

    /// <summary>
    /// Hands the scene down to the three layers - <b>with the picture in the hand at the values the
    /// hand has</b>, not at the ones that have come back from the hub.
    /// <para>
    /// It is the same local hold the table keeps while a finger is on a picture. Without it the
    /// drawing would step twenty times a second while the hand moves smoothly, and the DM would be
    /// watching the throttle rather than the table.
    /// </para>
    /// </summary>
    private void Draw()
    {
        var scene = _hold is { } hold
            ? _scene with
            {
                Items = [.. _scene.Items.Select(item => item.ItemId == hold.Item.ItemId ? hold.Item : item)],
            }
            : _scene;

        _thumbnail.Show(scene, _screen, _view);
        _loading.Show(scene, _screen, _view);
        _marks.Show(scene, _screen, _view);

        Redraw.Ask(_thumbnail);
    }

    /// <summary>Which place on the table a point on this panel means.</summary>
    private CorePoint Where(TilePoint at) => Placing.InScene(OnFace(at), _view, Wanted(RenderSize));

    /// <summary>
    /// Takes hold of whatever lies under this place.
    /// <para>
    /// <b>A parked card is not taken.</b> At the table the fan is a gesture in two halves - run
    /// along it to choose, pull away to take - and neither half has been given a form in a tile this
    /// size. Until it has, the way back out of the fan in the thumbnail is the item menu, which is a
    /// grip that exists rather than one that half works.
    /// </para>
    /// </summary>
    private bool Grab(TilePoint at)
    {
        var place = Where(at);

        if (Picking.At(_scene, _screen, place) is not { } id
            || _scene.Items.FirstOrDefault(item => item.ItemId == id) is not { } picture
            || picture.Parked)
        {
            return false;
        }

        _hold = new Hold(picture, place, OnFace(at));

        // The first report of a gesture is the grab, and what is taken hold of comes to the front -
        // unless it is locked, which the hub decides rather than this (Part 3).
        Send(binding: false, grabbing: true);

        return true;
    }

    /// <summary>One step of a hand on a picture.</summary>
    private void Step(GestureStep step, double travelDip)
    {
        if (_hold is not { } hold)
        {
            return;
        }

        var (moved, turning) = CoreManipulation.Step(hold.Item, hold.Turning, step, _screen);

        hold.Item = moved;
        hold.Turning = turning;
        hold.Moved += travelDip;

        Draw();
        Send(binding: false, grabbing: false);
    }

    /// <summary>
    /// The hand let go: the angle settles onto a quarter turn if it is near one, or the picture
    /// turns to whoever tapped it twice - and the last report goes out whatever the throttle says.
    /// </summary>
    private void LetGo(double totalDip, bool turning)
    {
        if (_hold is not { } hold)
        {
            return;
        }

        var now = Environment.TickCount64;
        var travelled = Math.Max(hold.Moved, totalDip);

        var toMe = turning
            || (Tapping.IsTap(travelled, now - hold.Began)
                && _tapping.Twice(now, hold.TapDip.X, hold.TapDip.Y));

        hold.Item = toMe
            ? CoreManipulation.HoldAtEdge(
                hold.Item with { RotationDeg = CoreManipulation.TurnToMe(hold.Tap, _screen) },
                _screen)
            : CoreManipulation.Settle(hold.Item, _screen);

        Draw();
        Send(binding: true, grabbing: false);

        _hold = null;
    }

    /// <summary>
    /// Hands the local values of the held picture to the hub. <b>The scene never comes back from
    /// here</b> - it comes from the hub, and a stage that trusted its own command would drift from a
    /// second control changing the same table (rule 1).
    /// </summary>
    private void Send(bool binding, bool grabbing)
    {
        if (_hold is not { } hold
            || !_throttle.Allows(hold.Item.ItemId, Environment.TickCount64, binding))
        {
            return;
        }

        _ = _session.TransformItemAsync(
            _screenRef,
            new ItemTransform(
                hold.Item.ItemId,
                hold.Item.CenterX,
                hold.Item.CenterY,
                hold.Item.Scale,
                hold.Item.RotationDeg),
            fromTable: false,
            toFront: grabbing,
            CancellationToken.None);
    }

    private void Pressed(MouseButtonEventArgs pressed)
    {
        var at = pressed.GetPosition(this);

        _pressed = at;
        _mouseAt = at;
        _spent = false;

        CaptureMouse();

        if (pressed.ClickCount != 2)
        {
            return;
        }

        // A double click turns the picture to the DM, exactly as a double tap does at the table -
        // the platform's own count rather than a second stopwatch beside the touch one.
        _spent = true;

        if (Grab(at))
        {
            LetGo(0, turning: true);
        }
    }

    private void Dragged(MouseEventArgs moved)
    {
        if (moved.LeftButton is not MouseButtonState.Pressed || _pressed is not { } from || _spent)
        {
            return;
        }

        var now = moved.GetPosition(this);

        if (_hold is null && _framing is null)
        {
            // Nothing is taken hold of until the hand has actually travelled: a press that turns
            // into a tap must not have moved a picture on the way (Part 7).
            if (Math.Abs(now.X - from.X) + Math.Abs(now.Y - from.Y) <= Press.Tolerance)
            {
                _mouseAt = now;

                return;
            }

            if (!Grab(from))
            {
                // Free area, so this is a frame. The two never collide: a frame begins strictly
                // OUTSIDE a picture and taking hold strictly ON one (Part 7).
                Frame(from);
            }
        }

        if (_framing is not null)
        {
            _mouseAt = now;

            Framed(now);

            return;
        }

        if (_hold is not { } hold)
        {
            return;
        }

        var face = Wanted(RenderSize);
        var travel = Math.Abs(now.X - _mouseAt.X) + Math.Abs(now.Y - _mouseAt.Y);

        // Ctrl+drag turns - here as at the table (Part 6, Part 7). The right button carries no drag
        // anywhere in this program: telling a right-drag from a right-click over a threshold is the
        // sort of grip that goes one way sometimes and the other way at other times.
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? new GestureStep(0, 0, 1, Swept(hold.Item, OnFace(_mouseAt), OnFace(now), face), Centre(hold.Item))
            : Pushed(new Vector(now.X - _mouseAt.X, now.Y - _mouseAt.Y), face, Centre(hold.Item));

        _mouseAt = now;

        Step(step, travel);
    }

    private void Lifted(MouseButtonEventArgs released)
    {
        ReleaseMouseCapture();

        var at = released.GetPosition(this);
        var from = _pressed;

        _pressed = null;

        if (_hold is not null)
        {
            LetGo(from is { } start ? Math.Abs(at.X - start.X) + Math.Abs(at.Y - start.Y) : 0, turning: false);

            return;
        }

        if (_framing is not null)
        {
            Framing(at, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

            return;
        }

        if (_spent || from is not { } began)
        {
            return;
        }

        if (Math.Abs(at.X - began.X) + Math.Abs(at.Y - began.Y) <= Press.Tolerance)
        {
            Tap(OnFace(at), Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }
    }

    /// <summary>
    /// The wheel zooms about the CURSOR, so the point under the pointer stays under it. Which way
    /// is larger is the screen's own setting, because everybody has an opinion about a wheel
    /// (Part 6) - and it is that screen's setting rather than the control's, so the DM's wheel and a
    /// hand at the table agree.
    /// </summary>
    private void Wheel(MouseWheelEventArgs turned)
    {
        var at = turned.GetPosition(this);
        var standing = _hold is not null;

        if (!standing && !Grab(at))
        {
            return;
        }

        const double Notch = 1.1;

        var factor = turned.Delta > 0 == _screen.ScrollUpZoomsIn ? Notch : 1 / Notch;

        Step(new GestureStep(0, 0, factor, 0, Where(at)), travelDip: 0);

        if (!standing)
        {
            // A notch is not a hold: it has no beginning and no end, so it reports bindingly at
            // once. Kept as a hold, every click of the wheel would be a grab and a release, and the
            // picture would climb to the front on each one.
            LetGo(0, turning: false);
        }

        turned.Handled = true;
    }

    /// <summary>
    /// A finger went down: the clock for the menu starts, and it starts here rather than when a
    /// picture has been taken hold of - free area has a menu too.
    /// </summary>
    private void Held(TilePoint at) => _press.Down(at, () => Menu(at));

    /// <summary>
    /// A menu was asked for at this place. <b>The gesture is closed first</b>: the picture has
    /// already come to the front, which is right - a long press is a grip - but nothing more must
    /// happen to it while a menu stands open over it.
    /// </summary>
    private void Menu(TilePoint at)
    {
        var on = OnFace(at);
        var face = Wanted(RenderSize);
        var where = Placing.InScene(on, _view, face);

        if (_hold is not null)
        {
            LetGo(0, turning: false);
        }

        if (_framing is not null)
        {
            _framing = null;
            _marks.Frame(null);
        }

        Asked?.Invoke(this, new MenuAsk(on, where, Picking.At(_scene, _screen, where)));
    }

    private void Started(TilePoint origin)
    {
        _pressed = origin;
        _spent = false;

        Grab(origin);
    }

    private void Delta(ManipulationDeltaEventArgs moved)
    {
        moved.Handled = true;

        if (_hold is null)
        {
            // A finger that took hold of nothing draws a frame - once it has travelled far enough
            // to have meant one rather than a tap.
            if (_framing is null && _pressed is { } began)
            {
                var travelled = moved.CumulativeManipulation.Translation;

                if (Math.Abs(travelled.X) + Math.Abs(travelled.Y) > Press.Tolerance)
                {
                    Frame(began);
                }
            }

            Framed(moved.ManipulationOrigin);

            return;
        }

        var face = Wanted(RenderSize);
        var delta = moved.DeltaManipulation;
        var pushed = Placing.DeltaInScene(delta.Translation, _view, face);

        Step(
            new GestureStep(
                pushed.X,
                pushed.Y,
                (delta.Scale.X + delta.Scale.Y) / 2,
                delta.Rotation,
                Placing.InScene(OnFace(moved.ManipulationOrigin), _view, face)),
            Math.Abs(delta.Translation.X) + Math.Abs(delta.Translation.Y));
    }

    private void Completed(ManipulationCompletedEventArgs done)
    {
        done.Handled = true;

        var total = done.TotalManipulation.Translation;

        if (_hold is not null)
        {
            LetGo(Math.Abs(total.X) + Math.Abs(total.Y), turning: false);

            return;
        }

        if (_framing is not null)
        {
            // A finger has no Ctrl: on touch a frame always replaces, and adding is what the
            // selection circles are for (Part 7).
            Framing(done.ManipulationOrigin, adding: false);

            _pressed = null;

            return;
        }

        // A manipulation that took hold of nothing was a tap on free area - or on a selection
        // circle, which lies on a picture and is therefore asked first.
        if (_pressed is { } began && Math.Abs(total.X) + Math.Abs(total.Y) <= Press.Tolerance)
        {
            Tap(OnFace(began), adding: false);
        }

        _pressed = null;
    }

    /// <summary>
    /// Begins a frame from free tile area. The one-finger drag is free for it because the stage
    /// itself pans with two fingers and there is nothing on a tile to scroll (Part 7).
    /// </summary>
    private void Frame(TilePoint from)
    {
        _framing = Clamped(OnFace(from));

        _marks.Frame(new TileRect(_framing.Value, _framing.Value));
    }

    /// <summary>
    /// One step of a frame. <b>It stops at the edge of the tile, visibly.</b> The pointer may run
    /// out and come back - the rectangle is always the part inside - because a frame drawn over the
    /// neighbouring screen would say it was selecting there, and a selection does not cross screens
    /// (Part 3, Part 7).
    /// </summary>
    private void Framed(TilePoint at)
    {
        if (_framing is { } from)
        {
            _marks.Frame(Between(from, Clamped(OnFace(at))));
        }
    }

    /// <summary>
    /// The frame closed. <b>Letting go outside the tile finishes it rather than cancelling it</b>,
    /// and a frame that never grew past a twitch was a tap on free area, which clears (Part 7).
    /// </summary>
    private void Framing(TilePoint at, bool adding)
    {
        if (_framing is not { } from)
        {
            return;
        }

        var to = Clamped(OnFace(at));
        var face = Wanted(RenderSize);

        _framing = null;
        _marks.Frame(null);

        if (Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) <= Press.Tolerance)
        {
            _selection.Clear();
            Touched?.Invoke(this, EventArgs.Empty);

            return;
        }

        var caught = Picking.Within(
            _scene,
            _screen,
            Spanning(Placing.InScene(from, _view, face), Placing.InScene(to, _view, face)));

        if (adding)
        {
            _selection.Add(caught);
        }
        else
        {
            _selection.Set(caught);
        }

        Touched?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The rectangle between two corners, whichever way round they were dragged.</summary>
    private static TileRect Between(TilePoint from, TilePoint to) =>
        new(
            Math.Min(from.X, to.X),
            Math.Min(from.Y, to.Y),
            Math.Abs(to.X - from.X),
            Math.Abs(to.Y - from.Y));

    /// <summary>
    /// The same rectangle in the scene. <b>Two opposite corners rather than a turned rectangle</b>:
    /// the view turns by quarters, so the frame stays axis-parallel and only its corners change
    /// places.
    /// </summary>
    private static CoreRect Spanning(CorePoint from, CorePoint to) =>
        new(
            Math.Min(from.X, to.X),
            Math.Min(from.Y, to.Y),
            Math.Abs(to.X - from.X),
            Math.Abs(to.Y - from.Y));

    private TilePoint Clamped(TilePoint at)
    {
        var face = Wanted(RenderSize);

        return new TilePoint(
            Math.Clamp(at.X, 0, Math.Max(0, face.Width)),
            Math.Clamp(at.Y, 0, Math.Max(0, face.Height)));
    }

    /// <summary>
    /// One tap, and the cascade it runs through: the selection circle first, then the scene, then
    /// free area.
    /// <para>
    /// <b>The circle comes first because it lies ON a picture.</b> Asked the other way round it
    /// could never be reached - the picture under it would always answer - and the touch way of
    /// building a selection would silently be the mouse's Ctrl+click only (Part 7).
    /// </para>
    /// </summary>
    private void Tap(TilePoint at, bool adding)
    {
        var face = Wanted(RenderSize);

        if (_marks.CircleAt(at) is { } circled)
        {
            _selection.Toggle(circled);
        }
        else if (Picking.At(_scene, _screen, Placing.InScene(at, _view, face)) is { } item)
        {
            if (adding)
            {
                _selection.Toggle(item);
            }
            else
            {
                _selection.Only(item);
            }
        }
        else
        {
            // Free area clears it. Ctrl does not save it: a modifier that changed what an empty
            // place means would be a rule nobody could see (Part 7).
            _selection.Clear();
        }

        Touched?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A movement of the hand, in the terms the scene is written in.</summary>
    private GestureStep Pushed(Vector delta, Size face, CorePoint pivot)
    {
        var scene = Placing.DeltaInScene(delta, _view, face);

        return new GestureStep(scene.X, scene.Y, 1, 0, pivot);
    }

    /// <summary>
    /// The angle the mouse swept around the picture's centre. A mouse has one point, so there is no
    /// pinch to take an angle from - the centre is the only pivot that makes a drag read as a turn.
    /// <para>
    /// <b>Measured on the face and not in the scene</b>, because the face is where the hand is: it
    /// is a difference of two angles, so the quarter turns of the view cancel out of it and no
    /// inverse is needed. The anisotropy does not enter either - the face restores the aspect that
    /// normalised coordinates leave out.
    /// </para>
    /// </summary>
    private double Swept(SceneItem item, TilePoint from, TilePoint to, Size face)
    {
        var centre = Placing.InTile(new CorePoint(item.CenterX, item.CenterY), _view, face);

        var before = Math.Atan2(from.Y - centre.Y, from.X - centre.X);
        var after = Math.Atan2(to.Y - centre.Y, to.X - centre.X);

        return (after - before) * 180 / Math.PI;
    }

    private static CorePoint Centre(SceneItem item) => new(item.CenterX, item.CenterY);

    /// <summary>Where a menu was asked for, and what lies there.</summary>
    /// <param name="At">The place on the face, for putting the menu where the hand is.</param>
    /// <param name="Where">The same place on the table - what "turn to me" measures against.</param>
    /// <param name="Item">The picture under it, or <see langword="null"/> for free area.</param>
    internal sealed record MenuAsk(TilePoint At, CorePoint Where, ItemId? Item);

    /// <summary>
    /// The picture in the hand, and what the gesture has to remember about it. The same shape the
    /// table keeps: the local values are the truth for as long as the hand is on it.
    /// </summary>
    private sealed class Hold(SceneItem item, CorePoint tap, TilePoint tapDip)
    {
        internal SceneItem Item { get; set; } = item;

        internal Turning Turning { get; set; } = Turning.Beginning;

        /// <summary>How far the hand has travelled in DIP - a tap is a gesture that barely moved.</summary>
        internal double Moved { get; set; }

        internal long Began { get; } = Environment.TickCount64;

        /// <summary>Where it started, normalised, for "turn to me" - the edge nearest THAT point.</summary>
        internal CorePoint Tap { get; } = tap;

        internal TilePoint TapDip { get; } = tapDip;
    }
}
