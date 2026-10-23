using System.Windows;

namespace DnDOverlay.Display;

/// <summary>
/// The angle a mouse swept around an item's centre between two positions - Ctrl+drag turns a picture
/// (Part 6). A mouse has one point, so there is no pinch to take an angle from: the centre is the only
/// pivot that makes a drag read as a rotation.
/// </summary>
internal static class MouseTurn
{
    /// <summary>
    /// The swept angle in degrees, clockwise on the screen (y grows downwards), always the short way
    /// round - between -180 and 180. All three points in stage DIP.
    /// </summary>
    internal static double Swept(Point centre, Point from, Point to)
    {
        var before = Math.Atan2(from.Y - centre.Y, from.X - centre.X);
        var after = Math.Atan2(to.Y - centre.Y, to.X - centre.X);

        // Left of the centre the angle jumps from +180 to -180; a step across that line is a small
        // step, not nearly a whole turn the other way.
        return Math.IEEERemainder((after - before) * 180 / Math.PI, 360);
    }
}
