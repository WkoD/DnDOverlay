namespace DnDOverlay.Core;

/// <summary>
/// Turning a screen for the person looking at it. The table lies flat and the DM sits at one of its
/// four sides; the thumbnail is turned until it lies the way the table does, and from then on
/// "right" means the same thing in both places (Part 7).
/// <para>
/// <b>It is a property of the view and travels nowhere.</b> Nothing on the table moves when it
/// changes, no device is told, and a <c>ConfigUpdate</c> carrying one would be a mistake rather
/// than a feature (<see cref="ViewRotation"/>).
/// </para>
/// <para>
/// <b>Two directions, and the second is the one that is easy to forget.</b> Drawing goes from the
/// scene into the view; every INPUT goes back the other way, and a delta that skipped the inverse
/// would move a picture sideways at 90 degrees and backwards at 180. The pair lives here so that
/// the thumbnail, the hit test and the manipulation ask one matrix rather than three.
/// </para>
/// <para>
/// <b>Normalised coordinates are anisotropic, and that is not an oversight here.</b> X is a
/// fraction of the screen's width and Y of its height, so a quarter turn maps the unit square onto
/// itself and swaps what the two axes MEAN. The tile that draws the result therefore has the
/// screen's aspect ratio turned as well - <see cref="AspectRatioInView"/> is that half of the
/// answer, and without it the picture would be right and the frame around it wrong.
/// </para>
/// </summary>
public static class Viewing
{
    /// <summary>Where a point of the scene appears in the view.</summary>
    public static Point ToView(Point scene, ViewRotation view) => view switch
    {
        ViewRotation.Quarter => new Point(1 - scene.Y, scene.X),
        ViewRotation.Half => new Point(1 - scene.X, 1 - scene.Y),
        ViewRotation.ThreeQuarters => new Point(scene.Y, 1 - scene.X),
        _ => scene,
    };

    /// <summary>
    /// Where a point of the view lies in the scene - the inverse, and the one every hit test needs:
    /// the DM taps a place in the thumbnail and the question is which place on the table that is.
    /// </summary>
    public static Point ToScene(Point view, ViewRotation rotation) => rotation switch
    {
        ViewRotation.Quarter => new Point(view.Y, 1 - view.X),
        ViewRotation.Half => new Point(1 - view.X, 1 - view.Y),
        ViewRotation.ThreeQuarters => new Point(1 - view.Y, view.X),
        _ => view,
    };

    /// <summary>
    /// A movement, turned back into the scene. It is <see cref="ToScene"/> without the translation,
    /// and it is the whole of "a drag to the right moves the picture to the left" at 180 degrees -
    /// the sentence the milestone is signed off against (Part 10).
    /// </summary>
    public static Point DeltaToScene(Point delta, ViewRotation view) => view switch
    {
        ViewRotation.Quarter => new Point(delta.Y, -delta.X),
        ViewRotation.Half => new Point(-delta.X, -delta.Y),
        ViewRotation.ThreeQuarters => new Point(-delta.Y, delta.X),
        _ => delta,
    };

    /// <summary>
    /// An item's angle as the view shows it. A picture standing straight on a table seen upside
    /// down is upside down, and drawing it any other way would make the thumbnail a different
    /// table rather than the same one from another side.
    /// </summary>
    public static double AngleInView(double angleDeg, ViewRotation view) =>
        Normalise(angleDeg + (int)view);

    /// <summary>An angle taken from the view, back in the scene's terms.</summary>
    public static double AngleToScene(double angleDeg, ViewRotation view) =>
        Normalise(angleDeg - (int)view);

    /// <summary>
    /// The rectangle an unrotated placement occupies in the view: the centre turns, and on a
    /// quarter turn the two extents change places.
    /// <para>
    /// They change places rather than being scaled because of what normalised coordinates are: the
    /// unit square is the screen, and a turned screen is still the unit square - what was a
    /// fraction of the width is now a fraction of the height. The stretch that this leaves out is
    /// exactly the one <see cref="AspectRatioInView"/> puts back when the tile is measured.
    /// </para>
    /// </summary>
    public static Rect ToView(Rect scene, ViewRotation view)
    {
        var centre = ToView(new Point(scene.X + (scene.Width / 2), scene.Y + (scene.Height / 2)), view);

        var (width, height) = view is ViewRotation.Quarter or ViewRotation.ThreeQuarters
            ? (scene.Height, scene.Width)
            : (scene.Width, scene.Height);

        return new Rect(centre.X - (width / 2), centre.Y - (height / 2), width, height);
    }

    /// <summary>
    /// A rectangle of the view, <b>before the view's own turn is applied to it</b> - the box a
    /// renderer that turns by <see cref="AngleInView"/> has to draw into.
    /// <para>
    /// <b>Two turns, and only one of them may be in the rectangle.</b> A picture drawn from a view
    /// gets both: <see cref="ToView(Rect,ViewRotation)"/> puts its box where the view shows it, and
    /// the renderer then turns it by <see cref="AngleInView"/>, which carries the view's angle as
    /// well. Drawing into the mapped box and turning it again applies the view twice - the picture
    /// lands in the right place, turned, and squeezed into the box belonging to the other axis.
    /// Read at the table in a 90-degree view (Handlauf M4, dritter Lauf).
    /// </para>
    /// <para>
    /// It is the exact inverse of the swap in <see cref="ToView(Rect,ViewRotation)"/>: same centre,
    /// extents put back. <b>Whatever a renderer does NOT turn keeps the mapped box</b> - an
    /// axis-aligned outline over the scene must appear as the mapped box, which is why an outline
    /// stayed right while the pictures under it did not.
    /// </para>
    /// </summary>
    public static Rect BeforeTurn(Rect turned, ViewRotation view) =>
        view is ViewRotation.Quarter or ViewRotation.ThreeQuarters
            ? new Rect(
                turned.X + ((turned.Width - turned.Height) / 2),
                turned.Y + ((turned.Height - turned.Width) / 2),
                turned.Height,
                turned.Width)
            : turned;

    /// <summary>
    /// A point of the view, taken back out of the view's own turn about <paramref name="centre"/> -
    /// the companion to <see cref="BeforeTurn(Rect,ViewRotation)"/> for anything that has to line up
    /// with a box drawn that way.
    /// <para>
    /// Written as the four exact quarter turns rather than through a sine: these are the only four
    /// there are, and an exact swap cannot leave a mark half a pixel off the picture it belongs to.
    /// <b>The direction is fixed by the renderer</b>, which turns by <c>+view</c>; this undoes it.
    /// A half turn is the symmetric case and says nothing about that direction (Guide
    /// <c>C14</c>) - the quarter turns are where it is decided.
    /// </para>
    /// </summary>
    public static Point BeforeTurn(Point at, Point centre, ViewRotation view)
    {
        var (dx, dy) = (at.X - centre.X, at.Y - centre.Y);

        var (x, y) = view switch
        {
            ViewRotation.Quarter => (dy, -dx),
            ViewRotation.Half => (-dx, -dy),
            ViewRotation.ThreeQuarters => (-dy, dx),
            _ => (dx, dy),
        };

        return new Point(centre.X + x, centre.Y + y);
    }

    /// <summary>
    /// The shape the view has: the screen's own, or its reciprocal on a quarter turn. A 16:9 table
    /// seen from its short side is 9:16, and the tile has to be that shape or everything drawn in
    /// it is stretched.
    /// </summary>
    public static double AspectRatioInView(double aspectRatio, ViewRotation view) =>
        view is ViewRotation.Quarter or ViewRotation.ThreeQuarters
            ? aspectRatio <= 0 ? aspectRatio : 1 / aspectRatio
            : aspectRatio;

    /// <summary>Into 0..360, so that two angles that mean the same are the same number.</summary>
    private static double Normalise(double angleDeg)
    {
        var turned = angleDeg % 360;

        return turned < 0 ? turned + 360 : turned;
    }
}
