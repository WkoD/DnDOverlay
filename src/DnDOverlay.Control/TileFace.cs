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

    /// <summary>
    /// A picture that is to be selected the moment it arrives on this screen - carried over from
    /// another tile, whose patch may still be on its way (<see cref="SelectWhenItArrives"/>).
    /// </summary>
    private ItemId? _awaited;
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

    /// <summary>
    /// Where the hand was in the scene the last time it was over this tile, and for which grip. Kept
    /// with the grip rather than on its own, so that a value from an earlier grip can never be read
    /// into a later one.
    /// </summary>
    private (Hold Hold, CorePoint At)? _inside;

    /// <summary>
    /// While the hand is outside: which grip, and where in the picture the hand held it - the offset
    /// from the picture's centre to the hand, in scene units. The picture does not turn or scale
    /// while it waits, so the offset still fits when the hand comes back.
    /// </summary>
    private (Hold Hold, CorePoint Offset)? _away;
    private Behind? _behind;

    private TilePoint? _pressed;
    private TilePoint _mouseAt;
    private TilePoint? _framing;
    private Hold? _hold;

    /// <summary>The hand that is on the fan, while it is - see <see cref="Grip"/>.</summary>
    private Fanning? _fan;
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

        // Only while the button is still DOWN: letting go releases the capture too, and that release
        // is Lifted's to handle, which runs first and has already seen the button come up.
        LostMouseCapture += (_, lost) =>
        {
            if (Mouse.LeftButton is MouseButtonState.Pressed)
            {
                Interrupted(lost.GetPosition(this));
            }
        };
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
            clicked.Handled = true;

            // Mid-carry the right button ends the carry and asks for nothing: a menu opened there
            // would belong to whichever tile the pointer happens to be over, and it took the mouse
            // away from this one (fifth hand-run, 15.09.2026).
            if (_carrying)
            {
                Abandoned(clicked.GetPosition(this));

                return;
            }

            Menu(clicked.GetPosition(this));
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

            // Transparent rather than veiled: a dark film over the pictures made the background
            // harder to judge, not easier, and judging it is the whole point of the mode
            // (hand-run of M4, 38b).
            _thumbnail.Faded(value);
            _marks.Dimmed(value);
            Redraw.Ask(_thumbnail);
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

        // <b>Was weggelegt wird, ist nicht mehr ausgewählt.</b> Ein Rahmen um eine Karte im Fächer
        // markiert etwas, woran der DM nicht mehr arbeitet - und er blieb dort durch alles hindurch
        // stehen, was der Fächer danach tat (Handlauf M4, dritter Lauf). Gefragt wird der ÜBERGANG
        // und nicht der Zustand: Geparktsein verbietet die Auswahl nicht, das Menü wählt den ganzen
        // Fächer auf einen Griff aus.
        foreach (var away in scene.Items)
        {
            if (away.Parked
                && _scene.Items.Any(was => was.ItemId == away.ItemId && !was.Parked))
            {
                _selection.Drop(away.ItemId);
            }
        }

        _scene = scene;
        _screen = screen;
        _view = view;

        // A picture that has left this screen is no longer selected - a menu command to an item
        // that is not there would be ineffective at the hub and a broken promise in the surface.
        _selection.Keep(scene);

        // And one carried in from another tile is, as soon as it is here.
        if (_awaited is { } awaited && scene.Items.Any(lying => lying.ItemId == awaited))
        {
            _awaited = null;
            _selection.Only(awaited);
        }

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
        // What is in the hand is drawn on top for as long as the hand is on it. The hub has
        // already been told and its answer will say the same, but it arrives a round trip later -
        // and until the hand-run the picture stayed under its neighbours until the moment it was
        // let go (hand-run of M4, 20).
        var scene = _hold is { } hold
            ? _scene with
            {
                Items =
                [
                    .. _scene.Items.Select(item => item.ItemId == hold.Item.ItemId
                        ? hold.Item with { ZOrder = _scene.Top(hold.Item.Parked) + 1 }
                        : item),
                ],
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

    /// <summary>
    /// A hand has landed on the fan: picks the card that place means and steps it out for a look.
    /// <para>
    /// <b>On the press, and that is the half the thumbnail was missing.</b> The table shows the card
    /// the moment a finger lands on the bar, and from there the gesture is either a run along the
    /// fan or a pull away from it (<c>OverlayWindow.Grip</c>). In the tile a card used to come out
    /// the instant it was touched, so there was neither a look nor a way to leaf along the row -
    /// reported at the table in the second hand-run of M4.
    /// </para>
    /// <para>
    /// Every number in it comes from <see cref="Parking"/>, the same one the table asks: which card
    /// a place means, where that card steps out to, and where the bar ends. A tile is a fifth of
    /// the size, and that changes what the hand can hit - it does not change what the fan IS
    /// (rule 9).
    /// </para>
    /// </summary>
    private bool Grip(TilePoint at)
    {
        if (Adjusting)
        {
            return false;
        }

        var place = Where(at);

        if (!Parking.OnTheFan(place, _screen)
            || Parking.Pick(_scene, _screen, place) is not { } card)
        {
            return false;
        }

        Fan(card);

        return true;
    }

    /// <summary>Begins looking at one card, whichever way the hand arrived at it.</summary>
    private void Fan(ItemId card)
    {
        _fan = new Fanning(card);

        _thumbnail.Peeking(card);
        _marks.Peeking(card);
        Redraw.Ask(_thumbnail);
    }

    /// <summary>
    /// One step of a hand that is on the fan: run along it and the card shown changes, leave it and
    /// that card comes out onto the table.
    /// </summary>
    /// <returns>Whether the gesture is still the fan's - <see langword="false"/> once it is not.</returns>
    private bool Fanned(TilePoint at)
    {
        if (_fan is not { } fanning)
        {
            return false;
        }

        var now = Where(at);

        if (Parking.OnTheFan(now, _screen))
        {
            if (Parking.Pick(_scene, _screen, now) is { } next && next != fanning.Card)
            {
                fanning.Card = next;

                // At the card's OWN place along the fan. The table learnt this the hard way: shown
                // under the hand it makes the eye chase it, shown where the hand landed the fan
                // turns into a slide viewer (Parking.Peek).
                _thumbnail.Peeking(next);
                _marks.Peeking(next);
                Redraw.Ask(_thumbnail);
            }

            return true;
        }

        // The hand has left the band, so the card comes out - and <b>that boundary and no other</b>,
        // because it is the same line that decides parking. The fan owns exactly the band and the
        // table owns the rest.
        //
        // It keeps the place the look gave it, so nothing jumps and the hand simply carries on.
        if (_scene.Items.FirstOrDefault(one => one.ItemId == fanning.Card) is not { } card
            || Parking.Peek(_scene, _screen, fanning.Card) is not { } peek)
        {
            Unfan();

            return false;
        }

        _ = _session.ParkItemAsync(_screenRef, fanning.Card, parked: false, CancellationToken.None);

        _hold = new Hold(card with { CenterX = peek.X, CenterY = peek.Y, Parked = false }, now, at);

        // Putting a card away took its outline off; taking it back out is taking hold of it, and
        // it gets one like anything else a hand presses (fourth hand-run, 15.09.2026).
        Chosen(card.ItemId);

        Unfan();
        Send(binding: false, grabbing: true);

        return false;
    }

    /// <summary>The end of a fan gesture: the card that was being looked at lies back down.</summary>
    private void Unfan()
    {
        if (_fan is null)
        {
            return;
        }

        _fan = null;

        _thumbnail.Peeking(null);
        _marks.Peeking(null);
        Redraw.Ask(_thumbnail);
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
        // Taken now: a held step is sent from a timer's thread, and must not read the gesture then.
        var session = _session;
        var screen = _screenRef;
        var centre = new CorePoint(behind.Background.CenterX, behind.Background.CenterY);
        var scale = behind.Background.Scale;
        var rotation = behind.Background.RotationDeg;

        // Under the empty id, which no item can have: a screen carries exactly one background, so
        // one entry is all it needs - and sharing an item's key would let a picture and the layer
        // under it throttle each other.
        _throttle.Report(
            default,
            binding ? ReportKind.Final : ReportKind.Step,
            () => _ = session.TransformBackgroundAsync(screen, centre, scale, rotation, CancellationToken.None));
    }

    /// <summary>
    /// Takes hold of whatever lies under this place.
    /// <para>
    /// <b>A parked card is not taken here.</b> The fan is a gesture in two halves - run along it to
    /// choose, pull away to take - and it is entered on the press, before anything is taken hold of
    /// at all (<see cref="Grip"/>). Taking a card here as well would be a second way into the fan,
    /// and it was the one the table complained about: a card came out the instant it was touched,
    /// so there was nothing to look at and nothing to leaf through.
    /// </para>
    /// </summary>
    private bool Grab(TilePoint at, bool selecting = true)
    {
        var place = Where(at);

        if (Picking.At(_scene, _screen, place) is not { } id
            || _scene.Items.FirstOrDefault(item => item.ItemId == id) is not { } picture)
        {
            return false;
        }

        if (picture.Parked)
        {
            // <b>A card is handed to the fan, not refused.</b> Refusing it let a selection frame
            // begin on a picture - and a frame begins strictly OUTSIDE one (Part 7). It happens
            // wherever a card's BODY reaches past the band, which is most of a long card: the point
            // picks the card, but it is not on the bar, so the fan gesture did not start either and
            // the drag became a rubber band from there to the tile edge (Handlauf M4, dritter Lauf).
            Fan(id);

            return false;
        }

        _hold = new Hold(picture, place, OnFace(at));

        if (selecting)
        {
            Chosen(id);
        }

        // The first report of a gesture is the grab, and what is taken hold of comes to the front -
        // unless it is locked, which the hub decides rather than this (Part 3).
        Send(binding: false, grabbing: true);

        return true;
    }

    /// <summary>
    /// Whether the hand is outside this tile with a picture in it - and while it is, the picture
    /// waits where the hand left and nothing is pushed.
    /// <para>
    /// <b>Leaving the tile no longer ends the grip</b> (fifth hand-run, 15.09.2026). It used to put
    /// the picture down at the edge and turn the gesture into a carry with no way back: a hand that
    /// came back over its own tile was still carrying, and could only let go. Now the hold stays
    /// alive. Outside, the picture waits at the place it had when the hand left - no step is taken,
    /// so Ctrl held for a copy cannot turn it either - and the stage shows the picture under the
    /// pointer. <b>Let go over another tile</b> and it is moved there, as before (Part 7).
    /// </para>
    /// <para>
    /// <b>Back inside, the picture comes back under the hand, held at the same point</b> (sixth
    /// hand-run, 15.09.2026). It used to carry on from where it had waited, so a hand that came back
    /// somewhere else pushed a picture that was no longer under it. The point in the picture the hand
    /// held when it left is kept, and on the way back in the picture is placed so that point is under
    /// the pointer again - then held at the edge like any other placement.
    /// </para>
    /// <para>
    /// <b>Both moments are reported at once, past the throttle.</b> The throttle lets the first report
    /// of every 50 ms through and drops the rest, with nothing trailing - so a hand leaving the tile
    /// took the last dropped steps with it, and the table stopped short of the tile, the further the
    /// faster the hand had been (sixth hand-run). The tile draws its own grip and looked right.
    /// </para>
    /// <para>
    /// <b>In the single view there is nowhere to carry to</b>, so leaving the tile shows nothing at
    /// all: the picture simply waits at its edge until the hand comes back or lets go. That is what
    /// "it cannot be pulled out" means from the hand's side.
    /// </para>
    /// </summary>
    private bool Left(TilePoint local)
    {
        if (_hold is not { } hold)
        {
            // The hold ended some other way while a carry was showing: take the ghost away with it.
            Abandoned(local, puttingDown: false);

            return false;
        }

        if (Over(local))
        {
            if (_away is { } away && ReferenceEquals(away.Hold, hold))
            {
                _away = null;

                Regripped(hold, local, away.Offset);

                if (_carrying)
                {
                    // Back over its own tile: no longer a carry, still a grip.
                    _carrying = false;

                    Carried?.Invoke(this, new Carry(default, null, PointToScreen(local), Phase.Ended, Copy: false));
                }

                // This event placed the picture. A step on top of it would move it twice.
                return true;
            }

            _inside = (hold, Where(local));

            return false;
        }

        if (_away is not { } gone || !ReferenceEquals(gone.Hold, hold))
        {
            // The moment the hand leaves: where in the picture it held it, from the last place it was
            // seen over the tile - or where the grip began, if the very first move already left.
            var held = _inside is { } seen && ReferenceEquals(seen.Hold, hold) ? seen.At : hold.Tap;

            _away = (hold, new CorePoint(held.X - hold.Item.CenterX, held.Y - hold.Item.CenterY));

            // And the table catches up with the tile before the picture starts waiting.
            Send(binding: false, grabbing: false, forced: true);
        }

        if (Opened)
        {
            return true;
        }

        if (_carrying)
        {
            Carried?.Invoke(this, new Carry(default, null, PointToScreen(local), Phase.Moved, Copy: false));

            return true;
        }

        var picture = hold.Item is ImageItem image ? _pictures.For(image.AssetId) : null;

        _carrying = true;

        Carried?.Invoke(this, new Carry(hold.Item.ItemId, picture, PointToScreen(local), Phase.Began, Copy: false));

        return true;
    }

    /// <summary>
    /// Puts a waiting picture back under a hand that has come back over its tile, held at the point
    /// it was held at when the hand left, and reports it at once.
    /// </summary>
    private void Regripped(Hold hold, TilePoint local, CorePoint offset)
    {
        var at = Where(local);

        hold.Item = CoreManipulation.HoldAtEdge(
            hold.Item with { CenterX = at.X - offset.X, CenterY = at.Y - offset.Y },
            _screen);

        _inside = (hold, at);

        Draw();
        Send(binding: false, grabbing: false, forced: true);
    }

    /// <summary>
    /// Whether a place is on this tile's face. Asked where the hand IS rather than remembered from
    /// where it was, so no flag can be left standing by a hold that ended some other way.
    /// </summary>
    private bool Over(TilePoint local)
    {
        var face = Wanted(RenderSize);
        var on = OnFace(local);

        return on.X >= 0 && on.Y >= 0 && on.X <= face.Width && on.Y <= face.Height;
    }

    /// <summary>
    /// The hand let go outside its tile. The picture is put down where it waited - <b>never into the
    /// fan</b>, because letting go on the fan is a statement made with the hand ON the tile, and this
    /// hand is not - and then the stage is told, and moves it if another tile lies underneath.
    /// <para>
    /// Put down first and moved second, and the order is safe either way round at the hub: a
    /// transform that arrives after the move finds the picture gone from this screen and does
    /// nothing (Part 11).
    /// </para>
    /// </summary>
    private void Dropped(TilePoint local)
    {
        if (!_carrying)
        {
            return;
        }

        _carrying = false;

        if (_hold is not null)
        {
            LetGo(0, turning: false, parking: false);
        }

        Carried?.Invoke(
            this,
            new Carry(
                default,
                null,
                PointToScreen(local),
                Phase.Dropped,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control)));
    }

    /// <summary>
    /// A carry that ends without being let go of - the right button pressed mid-carry, the mouse
    /// captured away, or the hold gone underneath it. The picture stays on its own screen, put down
    /// where it waited, and the ghost is taken off the stage.
    /// <para>
    /// <b>Found at the fifth hand-run:</b> a right click while carrying opened the context menu,
    /// the menu took the mouse, and the left button's release never reached this tile. The carry
    /// was never ended, so the ghost stood where the click had been, and the next grip on the
    /// original was a carry at once, even inside its own tile.
    /// </para>
    /// </summary>
    private void Abandoned(TilePoint local, bool puttingDown = true)
    {
        if (!_carrying)
        {
            return;
        }

        _carrying = false;

        // <b>The press is spent</b> (sixth hand-run, 15.09.2026). The left button is still down after a
        // right click, and a press that is not spent goes on meaning something: the next move started
        // a grip or a frame from where the press BEGAN, and letting go became a tap that selected.
        _spent = true;
        _pressed = null;

        if (puttingDown && _hold is not null)
        {
            LetGo(0, turning: false, parking: false);
        }

        Carried?.Invoke(this, new Carry(default, null, PointToScreen(local), Phase.Ended, Copy: false));
    }

    /// <summary>
    /// The mouse was taken away while the left button was still down - a menu, a dialog, another
    /// window. Nothing will tell this tile that the hand let go, so it lets go now: a carry ends as
    /// abandoned, a grip is put down where it lies. Neither is a statement, so neither parks.
    /// </summary>
    private void Interrupted(TilePoint local)
    {
        if (_carrying)
        {
            Abandoned(local);
        }
        else if (_hold is not null || _behind is not null)
        {
            LetGo(0, turning: false, parking: false);
        }

        _spent = true;
        _pressed = null;
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
    /// <param name="parking">
    /// Whether lying on the fan when the hand opens means "put it away". Only for a hand that let
    /// go on this tile: off the tile, or interrupted, the picture is put down where it is.
    /// </param>
    private void LetGo(double totalDip, bool turning, bool parking = true)
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
        // Let go on the fan: put away, and nothing else. It is the same rule the mouse follows at
        // the table - a pointer cannot flick, so lying on the fan when the hand opens IS the
        // statement (Part 6) - and it gives the thumbnail the way INTO the fan that the hand-run
        // asked for.
        if (parking && Parking.OnTheFan(new CorePoint(hold.Item.CenterX, hold.Item.CenterY), _screen))
        {
            _ = _session.ParkItemAsync(_screenRef, hold.Item.ItemId, parked: true, CancellationToken.None);

            _throttle.Forget(hold.Item.ItemId);
            _hold = null;

            Draw();

            return;
        }

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
    /// <param name="forced">
    /// At once, for a moment the table must not miss: the hand leaving the tile or coming back to it.
    /// A held step would reach the table too, but up to an interval later; this takes its place.
    /// </param>
    private void Send(bool binding, bool grabbing, bool forced = false)
    {
        if (_hold is not { } hold)
        {
            return;
        }

        // Taken now: a held step is sent from a timer's thread, and must not read the hold then.
        var session = _session;
        var screen = _screenRef;
        var transform = new ItemTransform(
            hold.Item.ItemId,
            hold.Item.CenterX,
            hold.Item.CenterY,
            hold.Item.Scale,
            hold.Item.RotationDeg);

        var kind = binding ? ReportKind.Final
            : grabbing || forced ? ReportKind.Urgent
            : ReportKind.Step;

        _throttle.Report(
            transform.Item,
            kind,
            () => _ = session.TransformItemAsync(
                screen,
                transform,
                fromTable: false,
                toFront: grabbing,

                // The control is not the table, so this changes no dispatch - but it says what the
                // report IS, and the throttle asks the same question.
                binding,
                CancellationToken.None));
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

        // The fan is entered on the press, before anything else is considered: a hand on the bar
        // is looking, not yet taking.
        if (Grip(at))
        {
            return;
        }

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

        if (_fan is not null)
        {
            if (Fanned(now))
            {
                _mouseAt = now;

                return;
            }

            // The card is out and the hand carries on with it from here.
            _mouseAt = now;
        }

        if (Left(now))
        {
            _mouseAt = now;

            return;
        }

        // <c>_behind is null</c> belongs in this condition, and its absence was the whole of the
        // mouse's background trouble: once the background had been taken hold of, every further
        // move ran through here again, took a FRESH hold and set the last position to the current
        // one - so the step below was computed from a distance of nothing, every time. The mouse
        // could zoom, because the wheel is a path of its own, and could do nothing else
        // (hand-run of M4, second run, 38b).
        if (_hold is null && _framing is null && _behind is null)
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
            else if (!Grab(from) && _fan is null)
            {
                // Free area, so this is a frame. The two never collide: a frame begins strictly
                // OUTSIDE a picture and taking hold strictly ON one (Part 7) - and a card of the
                // fan is a picture, however far from the bar the hand met it.
                Frame(from);
            }
        }

        if (_framing is not null)
        {
            _mouseAt = now;

            Framed(now);

            return;
        }

        if (_behind is not null)
        {
            // The background takes the same two grips as a picture. Until the hand-run the branch
            // below returned here because there was no HOLD, so with a mouse the background could
            // only be zoomed (hand-run of M4, 38b).
            var behind = Wanted(RenderSize);
            var turning = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var centre = BehindCentre;

            Step(
                turning
                    ? new GestureStep(0, 0, 1, Swept(centre, OnFace(_mouseAt), OnFace(now), behind), centre)
                    : Pushed(new Vector(now.X - _mouseAt.X, now.Y - _mouseAt.Y), behind, centre),
                travelDip: 0);

            _mouseAt = now;

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

        if (_fan is not null)
        {
            // Let go while still on the bar: the card lies back down and nothing happened. That is
            // the point of the two halves - looking is allowed to come to nothing.
            Unfan();

            return;
        }

        if (_carrying)
        {
            Dropped(at);

            return;
        }

        // <c>_behind</c> gehört in diese Bedingung, und sein Fehlen war der ganze Befund: Ein
        // Mausgriff auf dem Hintergrund legte eine lokale Kopie an, und losgelassen wurde nur, was
        // ein <c>_hold</c> war. Die Kopie blieb also stehen und überzeichnete von da an alles, was
        // vom Hub kam - „Bildschirm füllen" wirkte am Tisch und nicht in der Kachel, und der
        // nächste Zug sprang auf den alten Stand zurück (Handlauf M4, dritter Lauf, 38b).
        if (_hold is not null || _behind is not null)
        {
            // Parks only if the hand let go ON this tile. In the single view there is no carry, so a
            // hand that let go outside comes through here - and must not park either.
            LetGo(
                from is { } start ? Math.Abs(at.X - start.X) + Math.Abs(at.Y - start.Y) : 0,
                turning: false,
                parking: Over(at));

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

        // Turning the wheel over a picture zooms it and selects nothing: that is not pressing it.
        if (!standing && !Beneath(at) && !Grab(at, selecting: false))
        {
            return;
        }

        const double Notch = 1.1;

        var factor = turned.Delta > 0 == _screen.ScrollUpZoomsIn ? Notch : 1 / Notch;

        Step(new GestureStep(0, 0, factor, 0, Where(at)), travelDip: 0);

        if (!standing)
        {
            // Spent, so the release below cannot also count as a tap. Two notches of the wheel
            // within the double-tap window landed on the same spot and turned the picture to the
            // nearest edge - a grip nobody had asked for, in the middle of zooming (hand-run of
            // M4, 22).
            _spent = true;
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

        // <b>Das Menü wird an <c>at</c> gehängt, nicht an <c>on</c>.</b> Beide sind derselbe Ort,
        // aber in zwei Räumen: <c>on</c> ist um die Zentrierung der Miniatur verschoben, und die
        // Platzierung eines Kontextmenüs rechnet gegen dieses Element. In der Einzelansicht ist
        // diese Verschiebung breit, und das Menü erschien weit links vom Zeiger - so weit links,
        // wie die Miniatur mittig sitzt (Handlauf M4, dritter Lauf, 24a).
        Asked?.Invoke(this, new MenuAsk(at, where, Picking.At(_scene, _screen, where)));
    }

    /// <summary>
    /// Whether the space bar is asking for a spotlight. It is no global hotkey and works only while
    /// this window is in front, which a WPF key state already says.
    /// <para>
    /// <b>It used to ask what had keyboard focus, and that took the grip away.</b> The two limits
    /// were meant to keep the space bar out of a text field, where it writes a space, and off a
    /// focused button, which it presses - but they read the wrong thing. Focus is wherever the DM
    /// last clicked, and the stage is surrounded by buttons, so after any click on a grip the
    /// spotlight was simply gone, with nothing to see and nothing in the log. Reported in the
    /// second hand-run of M4 as "no spotlight at all".
    /// </para>
    /// <para>
    /// The rule the limits were reaching for is about the POINTER, not about focus: while the hand
    /// is over the stage, the space bar belongs to the stage. That is decided once, at the window,
    /// which also stops the key reaching the text field or the button in the first place -
    /// see <c>MainWindow</c>. Here nothing is left to ask but whether the key is down.
    /// </para>
    /// </summary>
    private static bool Pointing() => Keyboard.IsKeyDown(Key.Space);

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

    /// <summary>
    /// A finger has begun a manipulation. <b>Nothing is taken hold of here</b>, and that is the
    /// correction from the table: taking hold at once meant every touch ended as a release and
    /// never as a tap, so with a finger nothing could be selected at all while the mouse - which
    /// grabs only after travelling - worked (hand-run of M4, 25a). The background is the exception,
    /// because in its mode there is nothing else a finger could mean.
    /// </summary>
    private void Started(TilePoint origin)
    {
        _pressed = origin;
        _spent = false;

        if (Grip(origin))
        {
            return;
        }

        Beneath(origin);
    }

    private void Delta(ManipulationDeltaEventArgs moved)
    {
        moved.Handled = true;

        if (_fan is not null && Fanned(moved.ManipulationOrigin))
        {
            return;
        }

        if (_hold is null && _behind is null)
        {
            // Past the tolerance a finger has meant something other than a tap: the picture
            // under where it landed, or a frame from free area.
            if (_framing is null && _pressed is { } began)
            {
                var travelled = moved.CumulativeManipulation.Translation;

                if (Math.Abs(travelled.X) + Math.Abs(travelled.Y) > Press.Tolerance
                    && !Grab(began)
                    && _fan is null)
                {
                    Frame(began);
                }
            }

            if (_hold is null)
            {
                Framed(moved.ManipulationOrigin);

                return;
            }
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

        if (_fan is not null)
        {
            Unfan();

            return;
        }

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
            LetGo(Math.Abs(total.X) + Math.Abs(total.Y), turning: false, parking: Over(done.ManipulationOrigin));

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
    /// Selects a picture a hand has just taken hold of.
    /// <para>
    /// <b>Pressing a picture selects it, whether the hand then lets go or pulls</b> (fourth
    /// hand-run, 15.09.2026). A tap did this and a drag did not - the selection changed only in
    /// <see cref="Tap"/>, which a drag never reaches - so a picture clicked and released carried an
    /// outline and the same picture clicked and moved did not.
    /// </para>
    /// <para>
    /// <b>A picture that is already selected keeps the selection as it is.</b> The outline stays
    /// until ANOTHER picture is pressed, and pulling one member of a selection is not pressing
    /// another. With Ctrl held it joins the selection rather than replacing it - the mouse's way
    /// of choosing several, as on a tap. A finger has no Ctrl, and the selection circles are its
    /// way (Part 7).
    /// </para>
    /// </summary>
    private void Chosen(ItemId item)
    {
        if (_selection.Contains(item))
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _selection.Add([item]);
        }
        else
        {
            _selection.Only(item);
        }
    }

    /// <summary>
    /// Selects a picture on this screen as soon as it is here - now, if it already is.
    /// <para>
    /// Asked by the stage when a picture was carried in from another tile. <b>The patch that puts
    /// it here may not have arrived yet</b>, and selecting an id this scene does not hold would
    /// be undone by the very next <c>Show</c>, which drops whatever is not on the screen. So the
    /// wish waits for the picture rather than for a moment.
    /// </para>
    /// </summary>
    internal void SelectWhenItArrives(ItemId item)
    {
        if (_scene.Items.Any(lying => lying.ItemId == item))
        {
            _awaited = null;
            _selection.Only(item);

            return;
        }

        _awaited = item;
    }

    /// <summary>
    /// One tap: the picture under it, or free area.
    /// <para>
    /// <b>A tap on a picture also brings it to the front</b> - touching it in the thumbnail is the
    /// same statement as touching it at the table (hand-run of M4, 20).
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

        if (Picking.At(_scene, _screen, Placing.InScene(at, _view, face)) is { } item)
        {
            if (adding)
            {
                _selection.Toggle(item);
            }
            else
            {
                _selection.Only(item);
            }

            // The second tap on the same spot turns the picture to the DM, exactly as the mouse's
            // double click does. <b>It is asked here, and it has to be:</b> since the finger stopped
            // taking hold on touch-down (25a, first run), a plain tap makes no HOLD at all - and the
            // double tap was judged where a hold is released, which a tap never reaches. One fix
            // took the other out, which is the shape Guide C15 describes; the table found it in the
            // next run, as "double tap does not turn, double click does".
            Front(
                item,
                _tapping.Twice(Environment.TickCount64, at.X, at.Y)
                    ? Placing.InScene(at, _view, face)
                    : null);
        }
        else
        {
            // Free area clears it. Ctrl does not save it: a modifier that changed what an empty
            // place means would be a rule nobody could see (Part 7).
            _selection.Clear();
        }
    }

    /// <summary>
    /// Brings a picture to the front because it was touched. <b>A tap counts as taking hold</b>,
    /// not only a drag: tapping a picture in the thumbnail is the same statement as touching it at
    /// the table, and it made the menu entry "bring to front" unnecessary (hand-run of M4, 20).
    /// <para>
    /// It reports where the picture already lies; the hub hands out the depth, as it does for every
    /// other grab (rule 2).
    /// </para>
    /// </summary>
    /// <param name="turnTo">
    /// Where the tap landed, when it was the second of a double tap - the picture then turns to the
    /// edge nearest THAT point, which is the same arithmetic a released hold uses.
    /// </param>
    private void Front(ItemId item, CorePoint? turnTo = null)
    {
        if (_scene.Items.FirstOrDefault(one => one.ItemId == item) is not { Parked: false } picture)
        {
            return;
        }

        var placed = turnTo is { } tap
            ? CoreManipulation.HoldAtEdge(
                picture with { RotationDeg = CoreManipulation.TurnToMe(tap, _screen) },
                _screen)
            : picture;

        _ = _session.TransformItemAsync(
            _screenRef,
            new ItemTransform(item, placed.CenterX, placed.CenterY, placed.Scale, placed.RotationDeg),
            fromTable: false,
            toFront: true,

            // A finished placement, not the middle of one: this is called once, when the DM has
            // let go or tapped twice.
            binding: true,
            CancellationToken.None);
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
    private double Swept(SceneItem item, TilePoint from, TilePoint to, Size face) =>
        Swept(new CorePoint(item.CenterX, item.CenterY), from, to, face);

    /// <inheritdoc cref="Swept(SceneItem, TilePoint, TilePoint, Size)" />
    private double Swept(CorePoint about, TilePoint from, TilePoint to, Size face)
    {
        var centre = Placing.InTile(about, _view, face);

        var before = Math.Atan2(from.Y - centre.Y, from.X - centre.X);
        var after = Math.Atan2(to.Y - centre.Y, to.X - centre.X);

        return (after - before) * 180 / Math.PI;
    }

    private static CorePoint Centre(SceneItem item) => new(item.CenterX, item.CenterY);

    /// <summary>Where the background lies right now - the pivot a mouse turns it about.</summary>
    private CorePoint BehindCentre =>
        _behind is { } behind
            ? new CorePoint(behind.Background.CenterX, behind.Background.CenterY)
            : new CorePoint(0.5, 0.5);

    /// <summary>
    /// Where a place on the screen lands on this table, or <see langword="null"/> if it does not
    /// land on it at all.
    /// <para>
    /// <b>This is the target half of the hit test across tile borders</b>, and the order in it is
    /// the point: first which tile the hand is over, then the place within it - <b>through that
    /// tile's own view rotation</b>. A picture carried onto a table the DM looks at from the side
    /// has to land where he let go of it, not where the source tile would have put it (Part 7).
    /// </para>
    /// <para>
    /// <b>A tile that is not laid out answers "not on me", and that is an answer rather than an
    /// error.</b> In the single view only the open tile is in the tree at all - the others are not
    /// measured, arranged or drawn (<c>StageBoard.Lay</c>) - and <c>PointFromScreen</c> on a visual
    /// with no <c>PresentationSource</c> throws. The search for a drop target walks every tile, so
    /// carrying a picture OUT of the open one in the single view brought the control down
    /// (hand-run of M4, fourth run). It only happened there and only on the way out: as long as the
    /// hand is over the open tile, the search finds it first and never reaches a detached one.
    /// </para>
    /// </summary>
    internal CorePoint? Landing(TilePoint absolute)
    {
        if (!IsVisible)
        {
            return null;
        }

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
        /// <summary>The hand has left its tile with a picture in it.</summary>
        Began,

        /// <summary>It is still outside, somewhere else.</summary>
        Moved,

        /// <summary>It let go outside its tile, and the stage decides whether that lands anywhere.</summary>
        Dropped,

        /// <summary>
        /// The carry is over without landing: the hand came back over its own tile and goes on
        /// pushing, or the carry was abandoned. Either way the ghost goes and nothing is moved.
        /// </summary>
        Ended,
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
    /// <summary>
    /// A hand resting on the fan. It holds only which card is being looked at - the place that card
    /// steps out to is asked of <see cref="Parking.Peek"/> on every draw, so it follows the fan
    /// rather than being a second copy of it.
    /// </summary>
    private sealed class Fanning(ItemId card)
    {
        internal ItemId Card { get; set; } = card;
    }

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
