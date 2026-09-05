namespace DnDOverlay.Core;

/// <summary>
/// What lies under a place, and what a frame has caught - the two questions every grip in the
/// thumbnail starts with.
/// <para>
/// <b>One cascade, not two.</b> The fan is asked first and the items afterwards, in the same order
/// the drawing puts them in (<see cref="Parking.Depth"/>), because what is drawn and what is hit
/// have to be one calculation or they are two truths (Guide <c>G22</c>). M3 paid for that lesson
/// at the table: the fan laid its cards out by their LEADING edges and picked them by their
/// TRAILING ones, and with cards of unequal size one of them became invisible <i>and</i>
/// unreachable at the same time.
/// </para>
/// <para>
/// <b>It lives in <c>Core</c> although only the control asks it today</b>, for the reason
/// <see cref="Layout.ItemToRect"/> does: the table hit-tests through WPF elements, one per item,
/// and the thumbnail is a single drawn surface with no elements to test. Two surfaces showing the
/// same scene must agree about what is on top, and the only way to guarantee that is to compute it
/// once.
/// </para>
/// <para>
/// <b>Two forms of one question, and they answer it differently on purpose</b> (Part 7): a POINT
/// takes the topmost thing it touches, a FRAME takes everything it touches. Requiring a frame to
/// enclose a picture would be unusable in a tile the size of a thumbnail, where a turned or large
/// image can hardly be got inside one at all.
/// </para>
/// </summary>
public static class Picking
{
    /// <summary>
    /// The item under a place, or <see langword="null"/> for free area.
    /// <para>
    /// <b>Hidden items take no hits.</b> With the images switched off the tile shows an empty
    /// screen, and a grip that took hold of something invisible would move a picture the DM cannot
    /// see (Part 7).
    /// </para>
    /// <para>
    /// <b>Locked and parked items are found like any other.</b> The lock guards against the TABLE,
    /// not against the DM (Part 3), and a parked picture that could not be taken hold of would have
    /// no way back out of the fan.
    /// </para>
    /// </summary>
    public static ItemId? At(SceneState scene, ScreenContext screen, Point at)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        if (!scene.ItemsVisible)
        {
            return null;
        }

        // The fan lies above the whole table, in the thumbnail as at it (Part 7). It is asked
        // first for that reason alone: a picture put away is only ever wanted back, and a fan
        // underneath a full stage would be a drawer with a cupboard in front of it.
        if (Parking.Pick(scene, screen, at) is { } card)
        {
            return card;
        }

        return scene.Items
            .Where(item => !item.Parked)
            .OrderByDescending(item => item.ZOrder)
            .FirstOrDefault(item => Covers(item, screen, at))
            ?.ItemId;
    }

    /// <summary>
    /// Everything a frame has caught, bottom to top - the order they are drawn in, which is the
    /// order the DM sees them stacked.
    /// <para>
    /// <b>An order rather than a set</b>, because the selection is one (Part 3, Part 7): a focus
    /// shows its items in the order they were selected, and a set would look right until M5b put
    /// four pictures on a grid in whatever sequence a hash produced.
    /// </para>
    /// <para>
    /// <b>Measured against the same rotated hull as everywhere else</b> (Part 7, Part 6). The
    /// corner case the hull leaves open - a frame that overlaps the box but misses the picture in
    /// it - is accepted here and is not accepted for a point: catching one picture too many costs
    /// a tap to take it out again, while missing the one that was framed costs the whole gesture.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ItemId> Within(SceneState scene, ScreenContext screen, Rect frame)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        if (!scene.ItemsVisible)
        {
            return [];
        }

        return
        [
            .. scene.Items
                .Where(item => Touches(Layout.ItemToHullRect(item, screen), frame))
                .OrderBy(item => Parking.Depth(scene, item))
                .Select(item => item.ItemId),
        ];
    }

    /// <summary>
    /// Whether the point lies on the picture itself rather than merely in the box around it.
    /// <para>
    /// Against the four corners and not against the hull, because at 45 degrees the hull is half
    /// again as large as the picture and the difference is all corner - the place where two
    /// pictures lie side by side and the wrong one would answer.
    /// </para>
    /// </summary>
    private static bool Covers(SceneItem item, ScreenContext screen, Point at)
    {
        var quad = Layout.ItemToQuad(item, screen);

        // Same side of all four edges. Normalised coordinates stretch the picture but do not shear
        // it, so a point inside stays inside - the anisotropy that Layout has to undo for the
        // rotation itself does not enter here.
        var sign = 0;

        for (var i = 0; i < quad.Count; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % quad.Count];

            var side = ((b.X - a.X) * (at.Y - a.Y)) - ((b.Y - a.Y) * (at.X - a.X));

            if (side == 0)
            {
                continue;
            }

            var now = Math.Sign(side);

            if (sign != 0 && now != sign)
            {
                return false;
            }

            sign = now;
        }

        return true;
    }

    /// <summary>
    /// Whether two rectangles share any area. <b>Touching along an edge is not enough</b> - a frame
    /// dragged exactly along the top of a picture caught it, which reads as the frame reaching
    /// further than it is drawn.
    /// </summary>
    private static bool Touches(Rect item, Rect frame)
    {
        var overlap = item.Intersect(frame);

        return overlap.Width > 0 && overlap.Height > 0;
    }
}
