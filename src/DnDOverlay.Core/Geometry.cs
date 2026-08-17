using System.Globalization;

namespace DnDOverlay.Core;

/// <summary>
/// A point in normalised screen coordinates: 0…1 across the screen area (Part 3).
/// <para>
/// Geometry belongs to Core, which is why this type exists at all.
/// <c>System.Windows.Point</c> would drag PresentationCore into a library that must stay UI
/// free, and <c>System.Drawing.Point</c> is the second trap and looks harmless:
/// System.Drawing.Common has been Windows-only since .NET 7, so it is precisely the grip that
/// hands you the problem you were avoiding (Part 2, Part 3).
/// </para>
/// </summary>
public readonly record struct Point(double X, double Y)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:0.####}, {Y:0.####})");
}

/// <summary>
/// A rectangle in normalised screen coordinates. What <c>ItemToRect</c> produces, and the one
/// shape the display, the thumbnail and the edge clamp all compute against (Part 1, rule 9).
/// </summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>The overlap with another rectangle, or a zero-sized one if they do not touch.</summary>
    public Rect Intersect(Rect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return right <= left || bottom <= top
            ? new Rect(left, top, 0, 0)
            : new Rect(left, top, right - left, bottom - top);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{X:0.####}, {Y:0.####} {Width:0.####}x{Height:0.####}]");
}

/// <summary>
/// A size in PHYSICAL pixels - the one type here that is not normalised, because it describes
/// hardware rather than a placement.
/// </summary>
public readonly record struct PixelSize(int Width, int Height)
{
    /// <summary>Width divided by height. Zero height yields zero rather than an exception.</summary>
    public double AspectRatio => Height == 0 ? 0 : (double)Width / Height;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");
}
