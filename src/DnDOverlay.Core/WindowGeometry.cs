namespace DnDOverlay.Core;

/// <summary>Where on a foreign window's wireframe a hand took hold (Part 7): the title strip, a corner, or neither.</summary>
public enum WindowGrip
{
    None,
    Strip,
    Corner,
}

/// <summary>
/// Moving and sizing a foreign window's wireframe, in the screen's normalised space (Part 7). Taken
/// hold of <b>by its title strip</b> - the inside stays the selection frame's (DM, 21.10.2026) - and
/// by its corners. Everything here is in screen coordinates, so a tile turned by its view rotation
/// asks the same questions once it has turned the hand's place back.
/// </summary>
public static class WindowGeometry
{
    /// <summary>The smallest a window may be made, as a share of the screen - Windows enforces its own minimum besides.</summary>
    public const double Smallest = 0.05;

    /// <summary>
    /// Which part of a window a place is on. <paramref name="grip"/> is how deep the strip and the
    /// corners reach, as a share of the screen - a finger's width at the tile's size.
    /// </summary>
    public static WindowGrip At(Rect window, Point at, double grip)
    {
        var inside = at.X >= window.X && at.X <= window.X + window.Width && at.Y >= window.Y && at.Y <= window.Y + window.Height;

        if (!inside)
        {
            return WindowGrip.None;
        }

        var nearX = at.X - window.X <= grip || window.X + window.Width - at.X <= grip;
        var nearY = at.Y - window.Y <= grip || window.Y + window.Height - at.Y <= grip;

        if (nearX && nearY)
        {
            return WindowGrip.Corner;
        }

        return at.Y - window.Y <= grip ? WindowGrip.Strip : WindowGrip.None;
    }

    /// <summary>The window carried by the hand from <paramref name="from"/> to <paramref name="to"/> - its size unchanged.</summary>
    public static Rect Moved(Rect window, Point from, Point to) =>
        window with { X = window.X + (to.X - from.X), Y = window.Y + (to.Y - from.Y) };

    /// <summary>
    /// The window sized by the corner taken hold of at <paramref name="grabbed"/>: the corner opposite
    /// it stays where it is, the taken one follows the hand to <paramref name="now"/>, and the window
    /// keeps at least <see cref="Smallest"/> on each side - it does not turn inside out.
    /// </summary>
    public static Rect Resized(Rect window, Point grabbed, Point now)
    {
        var left = grabbed.X - window.X < window.X + window.Width - grabbed.X;
        var top = grabbed.Y - window.Y < window.Y + window.Height - grabbed.Y;
        var anchorX = left ? window.X + window.Width : window.X;
        var anchorY = top ? window.Y + window.Height : window.Y;

        var x = left ? Math.Min(now.X, anchorX - Smallest) : anchorX;
        var y = top ? Math.Min(now.Y, anchorY - Smallest) : anchorY;
        var right = left ? anchorX : Math.Max(now.X, anchorX + Smallest);
        var bottom = top ? anchorY : Math.Max(now.Y, anchorY + Smallest);

        return new Rect(x, y, right - x, bottom - y);
    }
}
