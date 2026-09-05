using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly Fingers _pointing = new();
    private readonly Selection _selection;
    private readonly Pictures _pictures;

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

    /// <summary>
    /// Every finger on this face, and how far each has travelled. It is the second reading the
    /// manipulation cannot give: a two-finger TAP and a two-finger DRAG arrive as the same
    /// manipulation, and only the count and the travel tell them apart.
    /// </summary>
    private readonly Dictionary<int, Finger> _fingers = [];

    private int _most;
    private long _landed;
    private double _travelled;
    private TilePoint _between;

    private bool _carrying;
    private Behind? _behind;

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
        _pictures = pictures;
        _thumbnail = new SceneThumbnail(pictures);
        _loading = new Loading(pictures);
        _marks = new Marks(selection);

        Children.Add(_thumbnail);
        Children.Add(_loading);

        // Above the marks: what somebody is pointing at is the most recent thing on the tile, and
        // it has to be readable over a selection outline (Part 7's layer order).
        Children.Add(_marks);
        Children.Add(_pointing);

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
        PreviewTouchDown += (_, down) =>
        {
            var at = down.GetTouchPoint(this).Position;

            Landed(down.TouchDevice.Id, at);
            Held(at);
        };

        PreviewTouchMove += (_, over) =>
        {
            var at = over.GetTouchPoint(this).Position;

            Travelled(over.TouchDevice.Id, at);
            _press.Moved(at);
        };

        PreviewTouchUp += (_, up) =>
        {
            _ = _fingers.Remove(up.TouchDevice.Id);
            _press.Up();
        };

        // A mouse asks for a menu with its right button, and never by holding the left one: what
        // "holding" means must not differ between a finger and a mouse (Part 7).
        PreviewMouseRightButtonUp += (_, clicked) =>
        {
            Menu(clicked.GetPosition(this));
            clicked.Handled = true;
        };

        // The middle button is the mouse's spotlight: a button of its own, immediate,
        // unmistakable, and unused anywhere else in this program. It leaves the held left button
        // free, which matters because "holding" would otherwise mean one thing to a finger and
        // another to a mouse (Part 7).
        PreviewMouseDown += (_, pressed) =>
        {
            if (pressed.ChangedButton is MouseButton.Middle)
            {
                Point(pressed.GetPosition(this));
                pressed.Handled = true;
            }
        };

        ManipulationStarted += (_, started) => Started(started.ManipulationOrigin);
        ManipulationDelta += (_, moved) => Delta(moved);
        ManipulationCompleted += (_, done) => Completed(done);
    }

    /// <summary>Raised when the background mode went on or off, so the menu can tick it.</summary>
    internal event EventHandler? Adjusted;

    /// <summary>
    /// A picture has left this tile in somebody's hand. From here on the stage carries it: the
    /// tile under the hand decides, and only then the place within it (Part 10 calls the hit test
    /// across tile borders one of the five biggest items in the plan).
    /// </summary>
    internal event EventHandler<Carry>? Carried;

    /// <summary>
    /// A menu was asked for: on a picture, or on free tile area. What the two contain is the tile's
    /// business, not the face's - the face knows where the hand was and what lies there.
    /// </summary>
    internal event EventHandler<MenuAsk>? Asked;

    /// <summary>
    /// Whether the hand on this tile is working the background rather than the pictures.
    /// <para>
    /// <b>A mode, and the only one on the stage.</b> Part 7 keeps modes out of the surface on
    /// purpose - selection is ordinary selection - and this is the exception it forces: the
    /// background is a layer without an item, it takes no touches at the table (Part 6), and every
    /// grip a tile has is already spoken for. A one-finger drag draws a frame, two fingers pan the
    /// stage, a long press opens a menu.
    /// </para>
    /// <para>
    /// <b>What makes it bearable is that it is visible and that it is asked for.</b> It is switched
    /// on from the screen menu and ticked there while it lasts, the pictures dim, the tile carries
    /// a border, and a tap that grips nothing ends it. A hidden modifier - Alt, a third finger -
    /// would be cheaper to build and impossible to find (decided at the end of M4c).
    /// </para>
    /// </summary>
    internal bool Adjusting
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _behind = null;

            _marks.Dimmed(value);
            InvalidateVisual();

            Adjusted?.Invoke(this, EventArgs.Empty);
        }
    }

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

    /// <summary>
    /// Where fingers are lying on this screen. Straight through as well, and for the opposite
    /// reason: this is the lowest rank there is (Part 4), so it is never worth holding on to.
    /// </summary>
    internal void Touching(IReadOnlyList<TouchTrail> touches) => _pointing.Report(touches);

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
            : _behind is { } behind
                ? _scene with { Background = behind.Background }
                : _scene;

        _thumbnail.Show(scene, _screen, _view);
        _loading.Show(scene, _screen, _view);
        _marks.Show(scene, _screen, _view);
        _pointing.Show(_screen, _view);

        Redraw.Ask(_thumbnail);
    }

    /// <summary>Which place on the table a point on this panel means.</summary>
    private CorePoint Where(TilePoint at) => Placing.InScene(OnFace(at), _view, Wanted(RenderSize));

    /// <summary>
    /// Takes hold of the background instead of a picture, while the mode is on. It answers
    /// everywhere on the tile - a layer that fills the screen has no free area beside it.
    /// </summary>
    private bool Beneath(TilePoint at)
    {
        if (!Adjusting || _scene.Background is not { } background)
        {
            return false;
        }

        _behind = new Behind(background);

        return true;
    }

    /// <summary>One step of a hand on the background - the same arithmetic, one layer down.</summary>
    private void Under(GestureStep step)
    {
        if (_behind is not { } behind)
        {
            return;
        }

        var (moved, turning) = CoreManipulation.Step(behind.Background, behind.Turning, step, _screen);

        behind.Background = moved;
        behind.Turning = turning;

        Draw();
        Send(behind, binding: false);
    }

    /// <summary>
    /// Hands the background's local values to the hub. <b>Throttled through the same table as an
    /// item</b>, under a key of its own: a screen has one background, so one entry is all it needs,
    /// and sharing an item's would let a picture and the layer under it throttle each other.
    /// </summary>
    private void Send(Behind behind, bool binding)
    {
        // Under the empty id, which no item can have: a screen carries exactly one background, so
        // one entry is all it needs - and sharing an item's key would let a picture and the layer
        // under it throttle each other.
        if (!_throttle.Allows(default, Environment.TickCount64, binding))
        {
            return;
        }

        _ = _session.TransformBackgroundAsync(
            _screenRef,
            new CorePoint(behind.Background.CenterX, behind.Background.CenterY),
            behind.Background.Scale,
            behind.Background.RotationDeg,
            CancellationToken.None);
    }

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

    /// <summary>
    /// Whether the hand has left this tile, and what follows from it: the picture stops here and
    /// the stage takes over carrying it.
    /// <para>
    /// <b>Leaving the tile is what turns a push into a move</b>, and that one rule saves a second
    /// grip: inside a tile a drag is a transform, past its edge it is <c>MoveItem</c> (Part 7).
    /// The picture is put down bindingly at the last place it had inside - if the carry comes to
    /// nothing, it lies where the hand left it rather than where it started.
    /// </para>
    /// </summary>
    private bool Left(TilePoint local)
    {
        if (_carrying)
        {
            Carried?.Invoke(this, new Carry(default, null, PointToScreen(local), Phase.Moved, Copy: false));

            return true;
        }

        if (_hold is not { } hold)
        {
            return false;
        }

        var face = Wanted(RenderSize);
        var on = OnFace(local);

        if (on.X >= 0 && on.Y >= 0 && on.X <= face.Width && on.Y <= face.Height)
        {
            return false;
        }

        var picture = hold.Item is ImageItem image ? _pictures.For(image.AssetId) : null;
        var item = hold.Item.ItemId;

        _carrying = true;

        LetGo(0, turning: false);

        Carried?.Invoke(this, new Carry(item, picture, PointToScreen(local), Phase.Began, Copy: false));

        return true;
    }

    /// <summary>The hand let go of what it was carrying, wherever that was.</summary>
    private void Dropped(TilePoint local)
    {
        if (!_carrying)
        {
            return;
        }

        _carrying = false;

        Carried?.Invoke(
            this,
            new Carry(
                default,
                null,
                PointToScreen(local),
                Phase.Dropped,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control)));
    }

    /// <summary>One step of a hand on a picture, or on the background while the mode is on.</summary>
    private void Step(GestureStep step, double travelDip)
    {
        if (_behind is not null)
        {
            Under(step);

            return;
        }

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
        if (_behind is { } behind)
        {
            // The same snap onto a quarter turn a picture gets, and the last report goes out
            // whatever the throttle says.
            behind.Background = CoreManipulation.Settle(behind.Background, _screen);

            Draw();
            Send(behind, binding: true);

            _behind = null;

            return;
        }

        if (_hold is not { } hold)
        {
            return;
        }

        var now = Environment.TickCount64;
        var travelled = Math.Max(hold.Moved, totalDip);

        // A gesture that was already spent - a double click, or a spotlight - does not also get
        // to be half of a double tap (Guide C16: the counter-check must not be greened by a path
        // that never ran).
        var toMe = turning
            || (!_spent
                && Tapping.IsTap(travelled, now - hold.Began)
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

        if (Pointing())
        {
            // Space and a left click, the grip MapTool uses for its own pointer: the DM has it in
            // his fingers already, and it closes the gap on pointing devices without a middle
            // button. With the space bar down the click ONLY lights up - it selects nothing, clears
            // nothing and begins no drag, or the pointing gesture would move a picture in passing
            // (Part 7).
            Point(at);

            _spent = true;
            _pressed = null;
            pressed.Handled = true;

            return;
        }

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

        if (Left(now))
        {
            _mouseAt = now;

            return;
        }

        if (_hold is null && _framing is null)
        {
            // Nothing is taken hold of until the hand has actually travelled: a press that turns
            // into a tap must not have moved a picture on the way (Part 7).
            if (Math.Abs(now.X - from.X) + Math.Abs(now.Y - from.Y) <= Press.Tolerance)
            {
                _mouseAt = now;

                return;
            }

            if (Beneath(from))
            {
                _mouseAt = now;
            }
            else if (!Grab(from))
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

        if (_carrying)
        {
            Dropped(at);

            return;
        }

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
        var standing = _hold is not null || _behind is not null;

        if (!standing && !Beneath(at) && !Grab(at))
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

    /// <summary>
    /// Whether the space bar is asking for a spotlight. <b>Three limits, or the space bar eats
    /// things that are not its own</b> (Part 7): it is no global hotkey and works only while this
    /// window is in front, which a WPF key state already says; and it stays out of the way of a
    /// text field, where space writes a space, and of a focused button, which space presses.
    /// </summary>
    private static bool Pointing() =>
        Keyboard.IsKeyDown(Key.Space)
        && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase
        && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase;

    /// <summary>
    /// Points at a place on this table. <b>Nothing is kept</b> - it changes no scene and takes no
    /// revision, and a device that misses it under load has missed a gesture the DM made mid
    /// sentence (Part 4, rank 4).
    /// </summary>
    private void Point(TilePoint at) =>
        _ = _session.SpotlightAsync(_screenRef, Where(at), CancellationToken.None);

    /// <summary>A finger landed. The first one starts the clock the two-finger tap is judged by.</summary>
    private void Landed(int finger, TilePoint at)
    {
        if (_fingers.Count == 0)
        {
            _landed = Environment.TickCount64;
            _most = 0;
            _travelled = 0;
        }

        _fingers[finger] = new Finger(at);
        _most = Math.Max(_most, _fingers.Count);

        if (_fingers.Count == 2)
        {
            // The point is the middle of the two fingers, taken when the second one lands: for
            // "look over HERE" that is close enough, and it does not wander while they lift
            // (Part 7).
            var both = _fingers.Values.ToList();

            _between = new TilePoint(
                (both[0].At.X + both[1].At.X) / 2,
                (both[0].At.Y + both[1].At.Y) / 2);
        }
    }

    private void Travelled(int finger, TilePoint at)
    {
        if (_fingers.TryGetValue(finger, out var known))
        {
            known.Moved += Math.Abs(at.X - known.At.X) + Math.Abs(at.Y - known.At.Y);
            known.At = at;

            // Kept beside the finger, not only on it: by the time the manipulation reports itself
            // finished the fingers are already gone from the table, and a travel read from an empty
            // one would make every two-finger drag look like a tap.
            _travelled = Math.Max(_travelled, known.Moved);
        }
    }

    /// <summary>
    /// Whether the gesture that has just ended was a two-finger tap rather than a two-finger drag.
    /// <para>
    /// <b>Decided at the end, on the count and the travel.</b> The two arrive as the same
    /// manipulation, and Part 7 has them mean two different things on purpose: the tap points, the
    /// drag pans - and the drag is the one that would collide if the tap were decided on the way
    /// down.
    /// </para>
    /// </summary>
    private bool Pointed()
    {
        return _most == 2 && Tapping.IsTap(_travelled, Environment.TickCount64 - _landed);
    }

    private void Started(TilePoint origin)
    {
        _pressed = origin;
        _spent = false;

        if (!Beneath(origin))
        {
            Grab(origin);
        }
    }

    private void Delta(ManipulationDeltaEventArgs moved)
    {
        moved.Handled = true;

        if (_hold is null && _behind is null)
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

        if (Left(moved.ManipulationOrigin))
        {
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

        if (_carrying)
        {
            Dropped(done.ManipulationOrigin);

            return;
        }

        if (Pointed())
        {
            // A two-finger tap points, wherever it lands - over a picture as well as on bare table
            // (Part 7). What was taken hold of is put down unchanged, and it must not count as a
            // tap on the way.
            Point(_between);

            _spent = true;
        }

        if (_hold is not null || _behind is not null)
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
        if (Adjusting)
        {
            // A tap that grips nothing ends the mode - the way out that needs no second control
            // and no memory (Part 7's habit for anything opened by a tap).
            Adjusting = false;

            return;
        }

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

    /// <summary>
    /// Where a place on the screen lands on this table, or <see langword="null"/> if it does not
    /// land on it at all.
    /// <para>
    /// <b>This is the target half of the hit test across tile borders</b>, and the order in it is
    /// the point: first which tile the hand is over, then the place within it - <b>through that
    /// tile's own view rotation</b>. A picture carried onto a table the DM looks at from the side
    /// has to land where he let go of it, not where the source tile would have put it (Part 7).
    /// </para>
    /// </summary>
    internal CorePoint? Landing(TilePoint absolute)
    {
        var on = OnFace(PointFromScreen(absolute));
        var face = Wanted(RenderSize);

        return on.X < 0 || on.Y < 0 || on.X > face.Width || on.Y > face.Height
            ? null
            : Placing.InScene(on, _view, face);
    }

    /// <summary>What a hand is carrying, and how far along that is.</summary>
    /// <param name="Item">The picture - meaningful when the carry begins.</param>
    /// <param name="Look">Its preview, for the ghost under the hand.</param>
    /// <param name="At">Where the hand is, in screen coordinates, because it crosses tiles.</param>
    /// <param name="Copy">Whether the drop was asked to copy rather than move.</param>
    internal sealed record Carry(ItemId Item, ImageSource? Look, TilePoint At, Phase Phase, bool Copy);

    /// <summary>How far along a carry is.</summary>
    internal enum Phase
    {
        Began,
        Moved,
        Dropped,
    }

    /// <summary>
    /// The background in the hand, and what its gesture has to remember. <b>Less than a picture's
    /// hold</b>: there is no "turn to me" for a layer nobody sits at the edge of, so it keeps no
    /// starting point - a field nothing reads is the category the TokenContainer came out of
    /// (checks/M2.md).
    /// </summary>
    private sealed class Behind(BackgroundItem background)
    {
        internal BackgroundItem Background { get; set; } = background;

        internal Turning Turning { get; set; } = Turning.Beginning;
    }

    /// <summary>One finger on the face: where it is now, and how far it has come.</summary>
    private sealed class Finger(TilePoint at)
    {
        internal TilePoint At { get; set; } = at;

        internal double Moved { get; set; }
    }

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
