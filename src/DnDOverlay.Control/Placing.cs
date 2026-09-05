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
