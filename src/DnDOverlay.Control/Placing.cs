using System.Windows;
using DnDOverlay.Core;
using CorePoint = DnDOverlay.Core.Point;
using CoreRect = DnDOverlay.Core.Rect;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// The one way between a scene and the surface it is drawn on: normalised screen coordinates on
/// the one side, DIP inside a tile on the other, with the view rotation in between.
/// <para>
/// <b>It exists because drawing and hitting must be one calculation</b> (Part 1, rule 9; Guide
/// <c>G22</c>). Four things now cross this boundary - the scene, the loading fill, the marks over
/// it and every finger that lands on a tile - and the last of them goes the other way. Four
/// private conversions would be four chances to get the sign of the inverse wrong, and the one
/// that got it wrong would be the one nobody sees until a picture walks the wrong way at 90
/// degrees (Guide <c>C14</c>: 180 degrees is the symmetric case and proves nothing about the
/// sign).
/// </para>
/// </summary>
internal static class Placing
{
    /// <summary>Where a normalised rectangle is drawn in a tile of this size.</summary>
    internal static TileRect InTile(CoreRect normalised, ViewRotation view, Size size)
    {
        var seen = Viewing.ToView(normalised, view);

        return new TileRect(
            seen.X * size.Width,
            seen.Y * size.Height,
            Math.Max(0, seen.Width * size.Width),
            Math.Max(0, seen.Height * size.Height));
    }

    /// <summary>
    /// The same rectangle <b>before</b> the view's own quarter turn is applied to it - what a
    /// picture is actually DRAWN into.
    /// <para>
    /// <b>Two turns, and only one of them may be in the rectangle.</b> A drawn picture gets both:
    /// <see cref="InTile(CoreRect,ViewRotation,Size)"/> maps its box through the view, and the
    /// renderer then turns it by <see cref="Viewing.AngleInView"/>, which carries the view's angle
    /// as well. Drawing into the mapped box and turning it again applies the view twice - the
    /// picture ends up in the right place, rotated, and squeezed into the box belonging to the
    /// other axis. Read at the table in a 90-degree view: <i>„die lange Seite ist auf Länge der
    /// Breite und die breite Seite auf Länge der Länge gezogen"</i> (Handlauf M4, dritter Lauf).
    /// </para>
    /// <para>
    /// So the box handed to the renderer is the one whose turn BY the view gives the mapped box:
    /// the same centre, the extents put back. <b>Whatever is not turned keeps the mapped box</b> -
    /// a selection outline is drawn axis-aligned in the scene and must appear as the mapped box,
    /// which is why it was the one thing on the tile that stayed right.
    /// </para>
    /// </summary>
    internal static TileRect BeforeTurn(TileRect mapped, ViewRotation view)
    {
        var before = Viewing.BeforeTurn(
            new CoreRect(mapped.X, mapped.Y, mapped.Width, mapped.Height), view);

        return new TileRect(before.X, before.Y, before.Width, before.Height);
    }

    /// <summary>
    /// A point of the mapped drawing, moved into the same space <see cref="BeforeTurn(TileRect,
    /// ViewRotation)"/> works in - so that a mask pushed inside the turn lines up with the box it
    /// belongs to. The arithmetic is <see cref="Viewing.BeforeTurn(CorePoint,CorePoint,
    /// ViewRotation)"/>; here is only the step between the two kinds of point.
    /// </summary>
    internal static TilePoint BeforeTurn(TilePoint at, TilePoint centre, ViewRotation view)
    {
        var before = Viewing.BeforeTurn(
            new CorePoint(at.X, at.Y), new CorePoint(centre.X, centre.Y), view);

        return new TilePoint(before.X, before.Y);
    }

    /// <summary>Where a normalised point is drawn in a tile of this size.</summary>
    internal static TilePoint InTile(CorePoint normalised, ViewRotation view, Size size)
    {
        var seen = Viewing.ToView(normalised, view);

        return new TilePoint(seen.X * size.Width, seen.Y * size.Height);
    }

    /// <summary>
    /// Which place on the screen a finger landed on. The inverse, and the one every grip in the
    /// thumbnail begins with.
    /// </summary>
    internal static CorePoint InScene(TilePoint at, ViewRotation view, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return default;
        }

        return Viewing.ToScene(new CorePoint(at.X / size.Width, at.Y / size.Height), view);
    }

    /// <summary>
    /// A movement, in the scene's terms. <b>Not <see cref="InScene"/> of two points</b>: the
    /// translation must not be applied to a difference, and at 180 degrees the two are hard to tell
    /// apart because the error cancels itself out.
    /// </summary>
    internal static CorePoint DeltaInScene(Vector delta, ViewRotation view, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return default;
        }

        return Viewing.DeltaToScene(new CorePoint(delta.X / size.Width, delta.Y / size.Height), view);
    }
}
