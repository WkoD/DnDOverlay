namespace DnDOverlay.Core;

/// <summary>
/// What a gesture carries between two of its steps. A dead zone is a statement about the whole
/// gesture, not about one delta, so somebody has to remember how far the fingers have turned so
/// far - and that somebody must not be a field in the display, or the thumbnail in M4 would need
/// a second one just like it.
/// </summary>
/// <param name="Pending">
/// Rotation collected while the dead zone still holds, in degrees. It is thrown away the moment
/// the zone is left, minus exactly the threshold.
/// </param>
/// <param name="Engaged">
/// Whether this gesture has left the dead zone. Once left it does not come back: below the
/// threshold the fingers would otherwise keep switching rotation on and off while the picture
/// sits still.
/// </param>
public readonly record struct Turning(double Pending, bool Engaged)
{
    /// <summary>A gesture that has just begun.</summary>
    public static Turning Beginning => default;
}

/// <summary>
/// One step of a manipulation, in the terms a touch or a mouse delivers it: how far the fingers
/// moved, how far they spread, how far they turned, and around which point.
/// </summary>
/// <param name="TranslationX">In normalised screen coordinates - a fraction of the screen WIDTH.</param>
/// <param name="TranslationY">In normalised screen coordinates - a fraction of the screen HEIGHT.</param>
/// <param name="ScaleFactor"><c>1</c> is unchanged. A pinch delivers the factor of this step, not the total.</param>
/// <param name="RotationDeg">Clockwise, and again the delta of this step alone.</param>
/// <param name="Origin">
/// The pivot: the pinch centre under two fingers, the cursor under the wheel. Scaling and turning
/// happen AROUND it, which is what stops a pinch from walking the picture away from the fingers -
/// the Java version turned and scaled around the item's centre, and a two-finger zoom slid the
/// picture out from under the hands.
/// </param>
public readonly record struct GestureStep(
    double TranslationX,
    double TranslationY,
    double ScaleFactor,
    double RotationDeg,
    Point Origin);

/// <summary>
/// The gesture arithmetic, as pure functions over an item and the screen it lies on.
/// <para>
/// <b>It lives here and not in the display</b>, for the reason <see cref="Layout.ItemToRect"/>
/// lives here: the thumbnail of M4 manipulates the same items with the same rules, and two
/// implementations of "how far may this be pushed" would differ on the day somebody fixed one of
/// them. What the applications keep is the translation of their own input - a
/// <c>ManipulationDelta</c> in device pixels, a wheel notch - into a <see cref="GestureStep"/>.
/// </para>
/// <para>
/// <b>Normalised coordinates are not isotropic</b>, and every rotation here has to say so: X is a
/// fraction of the screen width, Y a fraction of its height, so on a 16:9 table one normalised
/// unit sideways is 1.78 times the length of one downwards. Turning a vector means leaving that
/// system, turning, and coming back - <see cref="Pivot"/> is the one place that happens.
/// </para>
/// </summary>
public static class Manipulation
{
    /// <summary>
    /// How fast a picture has to be moving towards the park edge when it is let go for the swipe
    /// to count as parking rather than pushing, in DIP per second.
    /// <para>
    /// <b>Proposal until measured</b> (Part 6 names the rule and no number): a deliberate drag ends
    /// at a few hundred DIP per second, a flick at several thousand. What the number decides is
    /// which mistake happens - too low and every push to the edge parks, too high and parking never
    /// works and the players stop trying. The hand-run of M3b sets it, and then the NUMBER moves,
    /// not the test.
    /// </para>
    /// </summary>
    public const double ParkVelocityDip = 1000;

    /// <summary>
    /// How far a gesture may have travelled and still count as a flick, in DIP.
    /// <para>
    /// <b>A flick is speed AND a short way</b>, and the second half was missing (hand-run of M3,
    /// A2). Without it a long fast drag towards the park edge parked instead of merely moving the
    /// picture - which is the ordinary way to hand something across the table. Measured over the
    /// whole gesture, back and forth included: what is being asked is "was this a short jerk",
    /// and a hand that went out and came back was not.
    /// </para>
    /// <para>
    /// A proposal until the run has had fingers on it (Guide G6). The measured travel and speed of
    /// every release near the park edge go into the log (<c>3032</c>), so the number can be set
    /// from what a hand really does rather than from what a hand is imagined to do.
    /// </para>
    /// </summary>
    public const double ParkFlickDip = 400;

    /// <summary>
    /// Whether a hand at the table may take hold of this item at all.
    /// <para>
    /// <b>Three things suppress a gesture, and they are asked in ONE place</b> (Part 6): the
    /// padlock on the item, the screen state <c>Disabled</c>, and a focus lying on the screen. They
    /// are asked together because the answer to the finger is the same in all three cases - a
    /// player must not have to guess which of them it was - and because three separate checks are
    /// three places to forget the fourth.
    /// </para>
    /// <para>
    /// A focus suppresses the whole screen rather than the focused items: it is a way of PRESENTING
    /// one picture, and the table is being looked at, not arranged (Part 3). It cannot occur before
    /// M5b, and the field is asked for anyway - a condition that is checked from the day the field
    /// exists cannot be the one that gets forgotten when it starts being filled.
    /// </para>
    /// <para>
    /// The rescue marker is expressly not subject to this and lies above even a blackout (Part 6):
    /// it is the way out of a table that has stopped answering, so the one condition it must not
    /// have is "everything else works".
    /// </para>
    /// </summary>
    public static bool AcceptsGestures(SceneState scene, SceneItem item, ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(item);

        return state is not (ScreenState.Disabled or ScreenState.Blackout or ScreenState.Inactive)
            && scene.FocusItems.Count == 0
            && !item.Locked;
    }

    /// <summary>
    /// Applies one step of a gesture: turn, scale, move, and hold at the edge - in that order,
    /// because turning and scaling move the centre too when the pivot is not the centre.
    /// <para>
    /// Returns the <see cref="Turning"/> the next step has to be given. A caller that drops it gets
    /// a picture that never rotates, which is a visible bug rather than a silent one.
    /// </para>
    /// </summary>
    /// <param name="gliding">
    /// Whether this step is inertia rather than a hand. <b>A glide stops before the fan</b> and a
    /// hand may push right up to it - see <see cref="HoldAtEdge"/>.
    /// </param>
    public static (SceneItem Item, Turning Turning) Step(
        SceneItem item,
        Turning turning,
        GestureStep step,
        ScreenContext screen,
        bool gliding = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var (rotationDeg, next) = Turn(turning, step.RotationDeg, screen);

        var scale = Layout.ClampScale(item.Scale * step.ScaleFactor, item.AspectRatio, screen);

        // The factor that was actually granted, not the one that was asked for. At MaxScale a pinch
        // would otherwise keep dragging the centre away from the fingers while the picture stands
        // still.
        var factor = item.Scale <= 0 ? 1 : scale / item.Scale;

        var centre = Pivot(
            new Point(item.CenterX, item.CenterY),
            step.Origin,
            factor,
            rotationDeg,
            screen);

        var moved = item with
        {
            CenterX = centre.X + step.TranslationX,
            CenterY = centre.Y + step.TranslationY,
            Scale = scale,
            RotationDeg = Normalise(item.RotationDeg + rotationDeg),
        };

        return (HoldAtEdge(moved, screen, clearOfTheFan: gliding), next);
    }

    /// <summary>
    /// One step of a hand on the BACKGROUND layer - the same arithmetic as on a picture, because it
    /// carries a place and a size like one since M4 (Part 6).
    /// <para>
    /// <b>The stand-in is here rather than at the call site, and once.</b> Everything this needs -
    /// the dead zone, the pivot, the scale bounds, the edge - is written for a
    /// <see cref="SceneItem"/>, and a background is not one: it has no id, no ZOrder, no lock and
    /// no place in the fan, deliberately (Guide <c>C5</c>). Building the stand-in in the control
    /// would be a second gesture arithmetic in a surface, which is the thing rule 9 exists against.
    /// </para>
    /// <para>
    /// <b>The edge holds for it too</b>, and that is a decision rather than an oversight: a
    /// background pushed entirely off the screen would be a black table with an asset behind it
    /// and no way back except the two fit buttons. Whether it may be SMALLER than filling is a
    /// different question and already answered - <c>Contain</c> is a button (Part 6).
    /// </para>
    /// </summary>
    public static (BackgroundItem Background, Turning Turning) Step(
        BackgroundItem background, Turning turning, GestureStep step, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(screen);

        var (moved, next) = Step(Standing(background), turning, step, screen);

        // Held again, and by the BACKGROUND's rule: the step above ran the item clamp, which allows
        // a picture to hang out over the side. A background may not (hand-run of M4, 38b), and
        // asking here rather than at the two call sites is what keeps the hub and the hand from
        // clamping differently.
        return (HoldAtEdge(Taken(background, moved), screen), next);
    }

    /// <summary>What a release does to the background: the same snap onto a quarter turn.</summary>
    public static BackgroundItem Settle(BackgroundItem background, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(screen);

        return Taken(background, Settle(Standing(background), screen));
    }

    /// <summary>
    /// The background, held on the glass.
    /// <para>
    /// <b>It is held harder than a picture, and the rule is its own:</b> a background may not
    /// uncover an edge it is large enough to cover. A picture is allowed to hang out over the side
    /// - one zooms in to bring a detail closer - but a background that leaves a black stripe along
    /// the table is simply pushed too far, and there is nothing behind it to see (hand-run of M4,
    /// 38b). Whether it may be SMALLER than the screen is a different question and already
    /// answered: <c>Contain</c> is a button.
    /// </para>
    /// </summary>
    public static BackgroundItem HoldAtEdge(BackgroundItem background, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(screen);

        var held = Taken(background, HoldAtEdge(Standing(background), screen));
        var rect = Layout.BackgroundRect(held, screen);

        return held with
        {
            CenterX = Covering(held.CenterX, rect.X, rect.Width),
            CenterY = Covering(held.CenterY, rect.Y, rect.Height),
        };
    }

    /// <summary>
    /// One axis of the background, moved back just far enough that it still covers the screen -
    /// and left alone when it is too small to cover it at all.
    /// </summary>
    private static double Covering(double centre, double low, double extent)
    {
        if (extent < 1)
        {
            return centre;
        }

        var high = low + extent;

        return low > 0 ? centre - low
            : high < 1 ? centre + (1 - high)
            : centre;
    }

    /// <summary>
    /// The background as an item, for the length of one computation. Its shape comes from the asset
    /// rather than from a field of its own, which is the one place a background parts company with
    /// a picture (Part 3).
    /// </summary>
    private static ImageItem Standing(BackgroundItem background) =>
        new ImageItem(
            ItemId: default,
            CenterX: background.CenterX,
            CenterY: background.CenterY,
            Scale: background.Scale,
            AspectRatio: background.Meta.AspectRatio,
            RotationDeg: background.RotationDeg,
            ZOrder: 0,
            Locked: false,
            Parked: false,
            Revision: 0,
            AssetId: background.AssetId,
            Meta: background.Meta,
            Name: background.Name ?? string.Empty,
            ShowName: background.ShowName,
            AnimationPaused: background.AnimationPaused);

    private static BackgroundItem Taken(BackgroundItem background, SceneItem moved) =>
        background with
        {
            CenterX = moved.CenterX,
            CenterY = moved.CenterY,
            Scale = moved.Scale,
            RotationDeg = moved.RotationDeg,
        };

    /// <summary>
    /// What the fingers are allowed to turn in this step, and what the gesture has to remember.
    /// <para>
    /// Two fingers turn a picture a little every single time. Without a dead zone an evening of
    /// pushing leaves everything on the table slightly crooked, and nobody did it on purpose. When
    /// the zone is left the threshold is subtracted ONCE - otherwise the picture jumps by the whole
    /// dead zone at the moment it starts to move (Part 6).
    /// </para>
    /// </summary>
    public static (double RotationDeg, Turning Turning) Turn(Turning turning, double deltaDeg, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (turning.Engaged || screen.RotationDeadZoneDeg <= 0)
        {
            return (deltaDeg, new Turning(0, true));
        }

        var pending = turning.Pending + deltaDeg;

        if (Math.Abs(pending) <= screen.RotationDeadZoneDeg)
        {
            // Nothing turns, and translation and scaling of this step are untouched - the dead zone
            // is about rotation alone.
            return (0, new Turning(pending, false));
        }

        return (pending - (Math.Sign(pending) * screen.RotationDeadZoneDeg), new Turning(0, true));
    }

    /// <summary>
    /// What happens when the fingers leave: an angle close to a quarter turn is pulled onto it.
    /// <para>
    /// <b>Not during the gesture</b>, and that is the whole point of doing it here: a picture that
    /// clicks into place under the finger feels broken. These are exactly the angles "turn to me"
    /// produces, so no second frame of reference comes into being (Part 6).
    /// </para>
    /// </summary>
    public static SceneItem Settle(SceneItem item, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var settled = item with { RotationDeg = Snap(item.RotationDeg, screen) };

        // The hull of a snapped picture is a different one, so the edge has to be asked again -
        // a picture snapped from 87° to 90° can end up further out than it was allowed to be.
        return HoldAtEdge(settled, screen);
    }

    /// <summary>The angle a release settles on. A tolerance of <c>0</c> switches snapping off.</summary>
    public static double Snap(double rotationDeg, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (screen.RotationSnapToleranceDeg <= 0)
        {
            return Normalise(rotationDeg);
        }

        var normalised = Normalise(rotationDeg);
        var quarter = Math.Round(normalised / 90) * 90;

        return Math.Abs(normalised - quarter) <= screen.RotationSnapToleranceDeg
            ? Normalise(quarter)
            : normalised;
    }

    /// <summary>
    /// "Turn to me": the angle that puts the picture the right way up for somebody sitting at the
    /// edge nearest the point they tapped - the biggest single comfort gain on a table lying flat
    /// (Part 6).
    /// <para>
    /// Nearest is measured in DIP and not in normalised units, or the middle of a 21:9 table would
    /// count as being nearer the side than the top.
    /// </para>
    /// </summary>
    public static int TurnToMe(Point touch, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var width = screen.WidthInDip;
        var height = screen.HeightInDip;

        var toLeft = touch.X * width;
        var toRight = (1 - touch.X) * width;
        var toTop = touch.Y * height;
        var toBottom = (1 - touch.Y) * height;

        var nearest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));

        // Somebody at the bottom edge reads an upright picture, so that edge is 0° and the others
        // follow clockwise. Ties go to the bottom, then the sides: a tap in the exact centre is not
        // a request to turn the picture upside down.
        if (nearest == toBottom)
        {
            return 0;
        }

        if (nearest == toLeft)
        {
            return 90;
        }

        return nearest == toRight ? 270 : 180;
    }

    /// <summary>
    /// Whether letting go here parks the picture instead of leaving it lying at the edge.
    /// <para>
    /// <b>One condition, and it used to be two.</b> Part 6 also required the picture to be AT the
    /// park edge already - a flick from the middle was a push, and inertia carried it to the edge
    /// where it stopped. The table said otherwise: four people sit round it, and somebody at the
    /// far end had to shove a picture half an arm's length before the tidying gesture would even
    /// take (hand-run of M3, G30). Dropped at the end of M3, deliberately.
    /// </para>
    /// <para>
    /// <b>What the dropped condition was protecting, the speed still protects</b> - without a speed
    /// test one either parks by accident all evening or never manages to on purpose. And what it is
    /// no longer protecting is named: sliding a picture across the table to whoever sits AT the park
    /// edge now parks it instead of leaving it in front of them. That is the price, and it is on the
    /// closing run of M3.
    /// </para>
    /// </summary>
    /// <param name="velocityXDip">Speed at the moment of release, in DIP per second.</param>
    /// <param name="velocityYDip">Speed at the moment of release, in DIP per second.</param>
    /// <param name="travelDip">How far the hand went during the whole gesture, in DIP.</param>
    public static bool ShouldPark(
        SceneItem item,
        double velocityXDip,
        double velocityYDip,
        double travelDip,
        ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        return Towards(velocityXDip, velocityYDip, screen) >= ParkVelocityDip
            && travelDip <= ParkFlickDip;
    }

    /// <summary>How fast a release was heading for the park edge, in DIP per second.</summary>
    public static double Towards(double velocityXDip, double velocityYDip, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return screen.ParkEdge switch
        {
            ParkEdge.Left => -velocityXDip,
            ParkEdge.Right => velocityXDip,
            ParkEdge.Top => -velocityYDip,
            _ => velocityYDip,
        };
    }

    /// <summary>
    /// How far a point lies from the park edge, in DIP. Negative means past it, which a finger
    /// cannot be and a computed position can.
    /// </summary>
    public static double FromParkEdge(Point at, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return screen.ParkEdge switch
        {
            ParkEdge.Left => at.X * screen.WidthInDip,
            ParkEdge.Right => (1 - at.X) * screen.WidthInDip,
            ParkEdge.Top => at.Y * screen.HeightInDip,
            _ => (1 - at.Y) * screen.HeightInDip,
        };
    }

    /// <summary>
    /// Holds an item so that at least <see cref="ScreenContext.MinVisiblePixels"/> of it stay on
    /// the screen, measured against the ROTATED hull - at 45° the axis-parallel extent is a quite
    /// different thing (Part 6).
    /// <para>
    /// <b>Sticking out is expressly allowed</b>: under strong zoom one wants to bring a detail
    /// closer, and the rule is about vanishing, not about overhang. A picture smaller than the
    /// minimum has to stay whole - it cannot leave more behind than it has.
    /// </para>
    /// </summary>
    /// <param name="clearOfTheFan">
    /// Whether the fan counts as the edge of the screen on the park side, so that what is held ends
    /// up entirely clear of it.
    /// <para>
    /// <b>It is set for a glide and not for a hand</b> (hand-run of M3). A picture that slides to a
    /// stop with its whole remainder under the fan is parked without being parked: the fan is drawn
    /// over the table, so nothing of it can be reached. A hand is a different matter - somebody
    /// pushing a picture deliberately up to the fan meant to, and letting go there puts it in.
    /// </para>
    /// </summary>
    public static SceneItem HoldAtEdge(SceneItem item, ScreenContext screen, bool clearOfTheFan = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var hull = Layout.ItemToHullRect(item, screen);

        var (lowX, highX) = Fan(screen, clearOfTheFan, horizontal: true);
        var (lowY, highY) = Fan(screen, clearOfTheFan, horizontal: false);

        var held = item with
        {
            CenterX = Hold(item.CenterX, hull.Width, Visible(screen, horizontal: true), lowX, highX),
            CenterY = Hold(item.CenterY, hull.Height, Visible(screen, horizontal: false), lowY, highY),
        };

        return Reachable(held, screen);
    }

    /// <summary>
    /// How much of each end of an axis the fan takes up. Its depth is the graspable remainder
    /// itself - the cards lie exactly as far onto the glass as the clamp leaves anything else.
    /// </summary>
    private static (double Low, double High) Fan(ScreenContext screen, bool counting, bool horizontal)
    {
        if (!counting)
        {
            return (0, 0);
        }

        var band = Visible(screen, horizontal);

        return (screen.ParkEdge, horizontal) switch
        {
            (ParkEdge.Left, true) => (band, 0),
            (ParkEdge.Right, true) => (0, band),
            (ParkEdge.Top, false) => (band, 0),
            (ParkEdge.Bottom, false) => (0, band),
            _ => (0, 0),
        };
    }

    /// <summary>
    /// Pulls a picture back until a graspable patch of it is really on the glass.
    /// <para>
    /// <b>The clamp above is right at an edge and blind in a corner.</b> It holds each axis on its
    /// own against the hull, and at a corner both hold at once - satisfied by two DIFFERENT corners
    /// of a turned picture, with nothing between them. Measured at 37 degrees: both axes reporting
    /// their full 96 DIP and <b>zero</b> square DIP of picture on the screen (checks/M3.md, G31).
    /// </para>
    /// <para>
    /// So the area is asked afterwards, and only then: at an edge it costs one measurement and
    /// changes nothing, which is why the cheap clamp stays in front of it rather than being
    /// replaced. Where it does bite, the picture is drawn back along the line to the middle of the
    /// screen - the one direction that is always right, and the one a hand would take.
    /// </para>
    /// <para>
    /// <b>The threshold is the same promise the clamp makes, written as an area:</b> as much as a
    /// square of <see cref="ScreenContext.MinVisiblePixels"/> a side. For an UNTURNED picture pushed
    /// into a corner the clamp already leaves exactly that, so nothing changes for the case that
    /// worked - and a picture with less area than that has to stay whole, because it cannot leave
    /// more behind than it has.
    /// </para>
    /// </summary>
    private static SceneItem Reachable(SceneItem item, ScreenContext screen)
    {
        var patch = screen.MinVisiblePixels * screen.MinVisiblePixels;
        var rect = Layout.ItemToRect(item, screen);
        var whole = rect.Width * screen.WidthInDip * rect.Height * screen.HeightInDip;

        // A hair under the whole, and that hair is the whole fix. For a picture smaller than the
        // patch the requirement IS its whole area - and the two sides of the comparison are
        // computed by different routes, a rectangle here and a clipped polygon there, so they
        // differ in the last bits. The test then failed for a picture lying entirely on the glass,
        // and the search pulled it to the middle of the screen: a picture zoomed down small could
        // not be moved at all any more, it sprang back on every step (hand-run of M4, 21).
        var required = Math.Min(patch, whole * (1 - 1e-9));

        if (required <= 0 || Layout.VisibleAreaInDip(item, screen) >= required)
        {
            return item;
        }

        // Towards the middle, which always satisfies: a picture centred on the screen shows either
        // a full patch or the whole of itself. Halving the interval twenty times lands well inside
        // a pixel, and it is the same answer every time for the same input - a gesture must not
        // jitter because a search wandered.
        var (fromX, fromY) = (item.CenterX, item.CenterY);
        var (lo, hi) = (0d, 1d);

        for (var step = 0; step < 20; step++)
        {
            var middle = (lo + hi) / 2;
            var tried = item with
            {
                CenterX = fromX + ((0.5 - fromX) * middle),
                CenterY = fromY + ((0.5 - fromY) * middle),
            };

            if (Layout.VisibleAreaInDip(tried, screen) >= required)
            {
                hi = middle;
            }
            else
            {
                lo = middle;
            }
        }

        return item with
        {
            CenterX = fromX + ((0.5 - fromX) * hi),
            CenterY = fromY + ((0.5 - fromY) * hi),
        };
    }

    /// <summary>
    /// How much of an inertial push still gets through, between <c>1</c> well inside the screen and
    /// <c>0</c> at the point where the edge clamp would take over.
    /// <para>
    /// Friction that rises towards the edge, not a wall: a picture thrown with full force glides
    /// out, becomes noticeably slower and comes to rest with a graspable remainder showing. The
    /// clamp would stop it just as safely and it would feel like hitting a kerb, which on a table
    /// full of players reads as "it broke" (Part 6).
    /// </para>
    /// </summary>
    /// <param name="item">Where the picture is right now, mid-glide.</param>
    public static double EdgeResistance(SceneItem item, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        // Against the boundary a glide really has, which is the fan and not the screen - otherwise
        // the friction would still be building where the picture has already stopped.
        var held = HoldAtEdge(item, screen, clearOfTheFan: true);
        var hull = Layout.ItemToHullRect(item, screen);

        // How far past the allowed position it already is, as a fraction of the braking distance.
        // One hull is the distance over which the friction builds up: on a small picture that is a
        // short, firm brake, on a large one a long soft one, and both feel like the same table.
        var overrunX = hull.Width <= 0 ? 0 : Math.Abs(item.CenterX - held.CenterX) / hull.Width;
        var overrunY = hull.Height <= 0 ? 0 : Math.Abs(item.CenterY - held.CenterY) / hull.Height;

        return Math.Clamp(1 - Math.Max(overrunX, overrunY), 0, 1);
    }

    /// <summary>
    /// Moves a point around a pivot: scaled, turned, and back into normalised coordinates. The
    /// detour through isotropic units is not tidiness - a rotation applied to normalised offsets
    /// directly shears the picture across the table on anything that is not square.
    /// </summary>
    private static Point Pivot(Point point, Point origin, double factor, double rotationDeg, ScreenContext screen)
    {
        var aspect = screen.AspectRatio <= 0 ? 1 : screen.AspectRatio;

        // Into units of screen HEIGHT on both axes, where a rotation is a rotation.
        var x = (point.X - origin.X) * aspect * factor;
        var y = (point.Y - origin.Y) * factor;

        if (rotationDeg % 360 != 0)
        {
            var radians = rotationDeg * Math.PI / 180d;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            // Clockwise, because Y points down on a screen.
            (x, y) = ((x * cos) - (y * sin), (x * sin) + (y * cos));
        }

        return new Point(origin.X + (x / aspect), origin.Y + y);
    }

    /// <summary>
    /// How much of the hull has to remain on the screen along one axis, in that axis' own
    /// normalised unit. A picture narrower than the minimum owes only its own width.
    /// </summary>
    internal static double Visible(ScreenContext screen, bool horizontal) =>
        horizontal ? screen.MinVisibleNormalisedX : screen.MinVisibleNormalisedY;

    /// <summary>
    /// Holds one coordinate so the required part of the extent stays between the two ends, each of
    /// which may be moved inwards by a margin.
    /// </summary>
    private static double Hold(double centre, double extent, double minimum, double low, double high)
    {
        var required = Math.Min(minimum, extent);
        var slack = extent / 2;

        var lower = low + required - slack;
        var upper = 1 - high - required + slack;

        return Math.Clamp(centre, Math.Min(lower, upper), Math.Max(lower, upper));
    }

    /// <summary>Any angle as 0° up to but not including 360°, so that 359° and -1° are one value.</summary>
    internal static double Normalise(double degrees)
    {
        var turned = degrees % 360;

        return turned < 0 ? turned + 360 : turned;
    }
}
