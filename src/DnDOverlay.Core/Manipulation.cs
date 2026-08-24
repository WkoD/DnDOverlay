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
/// How hard the fingers are currently pushing a picture against the edge clamp, across the park
/// edge, in DIP. The second thing a gesture has to remember, and it is here beside
/// <see cref="Turning"/> for the same reason: the thumbnail in M4 must not need one of its own.
/// <para>
/// <b>A pressure, not a distance travelled.</b> It grows only while the fingers ask for outward
/// movement the clamp refuses, and it shrinks again the moment they come back - so a picture that
/// merely rests against the edge all evening never reaches the threshold, and one deliberately
/// shoved out does so in a single push.
/// </para>
/// </summary>
public readonly record struct Push(double Dip)
{
    /// <summary>A gesture that has not pushed against anything yet.</summary>
    public static Push Beginning => default;
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
    public static (SceneItem Item, Turning Turning, Push Push) Step(
        SceneItem item,
        Turning turning,
        Push push,
        GestureStep step,
        ScreenContext screen)
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

        var held = HoldAtEdge(moved, screen);

        return (held, next, Pushing(push, item, moved, held, screen));
    }

    /// <summary>
    /// Updates the pressure against the park edge by one step: what the fingers asked for outwards,
    /// minus what they were actually granted.
    /// <para>
    /// Written as one subtraction rather than a case analysis, and it holds in all four situations
    /// worth naming. Free movement outwards asks and is granted the same, so nothing builds up.
    /// Against the clamp nothing is granted, so the whole request builds up. Moving back inwards
    /// grants a negative amount, which the <c>Math.Max</c> keeps out of the deduction - so the
    /// pressure falls by exactly what the fingers came back, not by what the corner rule may have
    /// pulled on top of it. And it never goes below nought, because a picture in the middle of the
    /// table is not pushing against anything.
    /// </para>
    /// </summary>
    private static Push Pushing(
        Push push,
        SceneItem before,
        SceneItem wanted,
        SceneItem held,
        ScreenContext screen)
    {
        var (outX, outY) = screen.ParkEdge switch
        {
            ParkEdge.Left => (-1d, 0d),
            ParkEdge.Right => (1d, 0d),
            ParkEdge.Top => (0d, -1d),
            _ => (0d, 1d),
        };

        double Outwards(SceneItem to) =>
            ((to.CenterX - before.CenterX) * outX * screen.WidthInDip)
            + ((to.CenterY - before.CenterY) * outY * screen.HeightInDip);

        return new Push(Math.Max(0, push.Dip + Outwards(wanted) - Math.Max(0, Outwards(held))));
    }

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
    public static bool ShouldPark(
        SceneItem item,
        double velocityXDip,
        double velocityYDip,
        ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var towards = screen.ParkEdge switch
        {
            ParkEdge.Left => -velocityXDip,
            ParkEdge.Right => velocityXDip,
            ParkEdge.Top => -velocityYDip,
            _ => velocityYDip,
        };

        return towards >= ParkVelocityDip;
    }

    /// <summary>
    /// Whether the fingers have pushed this picture far enough past the edge clamp that letting go
    /// parks it. <c>ParkPushOutDip</c> of <c>0</c> switches the whole route off.
    /// <para>
    /// <b>The second way into the bar, and the only one a mouse has</b> (decided at the end of M3).
    /// A flick needs a velocity, and a mouse cannot give one that means anything; pushing a picture
    /// out over the edge is the same intention expressed slowly, and it works with either hand.
    /// </para>
    /// <para>
    /// <b>Only across the park edge.</b> Pushed out on the left and reappearing on the right would
    /// be bewildering, and there is exactly one bar per screen (Part 6).
    /// </para>
    /// </summary>
    public static bool PushedIntoTheBar(Push push, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return screen.ParkPushOutDip > 0 && push.Dip >= screen.ParkPushOutDip;
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
    public static SceneItem HoldAtEdge(SceneItem item, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(screen);

        var hull = Layout.ItemToHullRect(item, screen);

        var held = item with
        {
            CenterX = Hold(item.CenterX, hull.Width, Visible(screen, horizontal: true)),
            CenterY = Hold(item.CenterY, hull.Height, Visible(screen, horizontal: false)),
        };

        return Reachable(held, screen);
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
        var required = Math.Min(patch, whole);

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

        var held = HoldAtEdge(item, screen);
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

    /// <summary>Holds one coordinate so the required part of the extent stays between 0 and 1.</summary>
    private static double Hold(double centre, double extent, double minimum)
    {
        var required = Math.Min(minimum, extent);
        var slack = extent / 2;

        return Math.Clamp(centre, required - slack, 1 - required + slack);
    }

    /// <summary>Any angle as 0° up to but not including 360°, so that 359° and -1° are one value.</summary>
    internal static double Normalise(double degrees)
    {
        var turned = degrees % 360;

        return turned < 0 ? turned + 360 : turned;
    }
}
