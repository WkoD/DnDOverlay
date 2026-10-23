using System.Windows;

namespace DnDOverlay.Control;

/// <summary>
/// The angle a mouse sweeps around a pivot - Ctrl+drag turns a picture or the background on a tile
/// (Part 6, Part 7). A mouse has one point, so there is no pinch to take an angle from; the centre is
/// the only pivot that makes a drag read as a turn.
/// <para>
/// <b>Measured on the face and not in the scene</b>, because the face is where the hand is: it is a
/// difference of two angles, so the quarter turns of the view cancel out of it and no inverse is
/// needed. The anisotropy does not enter either - the face restores the aspect that normalised
/// coordinates leave out.
/// </para>
/// <para>
/// <b>The difference is wrapped into -180..180.</b> The angle of a point jumps from +180 to -180 on the
/// line left of the pivot, so one small step across it read as nearly a whole turn the other way: the
/// picture jumped by about 358 degrees and the rotation dead zone, which should have held, was blown
/// through. A step between two mouse moves is never more than half a turn.
/// </para>
/// </summary>
internal static class MouseTurn
{
    /// <summary>The angle in degrees from <paramref name="from"/> to <paramref name="to"/> about <paramref name="pivot"/>, clockwise on screen positive.</summary>
    internal static double Swept(Point pivot, Point from, Point to)
    {
        var before = Math.Atan2(from.Y - pivot.Y, from.X - pivot.X);
        var after = Math.Atan2(to.Y - pivot.Y, to.X - pivot.X);

        return Math.IEEERemainder((after - before) * 180 / Math.PI, 360);
    }
}
