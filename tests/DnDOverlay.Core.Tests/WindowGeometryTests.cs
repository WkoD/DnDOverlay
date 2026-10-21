namespace DnDOverlay.Core.Tests;

/// <summary>Taking hold of a foreign window's wireframe (Part 7): by its strip and its corners, never by its inside.</summary>
public sealed class WindowGeometryTests
{
    private static readonly Rect Window = new(0.2, 0.2, 0.4, 0.4);
    private const double Grip = 0.03;

    /// <summary>The strip along the top moves, the corners size, the inside belongs to the selection frame (DM, 21.10.2026).</summary>
    [Fact]
    public void Strip_and_corners_are_grips_and_the_inside_is_not()
    {
        Assert.Equal(WindowGrip.Strip, WindowGeometry.At(Window, new Point(0.4, 0.21), Grip));
        Assert.Equal(WindowGrip.Corner, WindowGeometry.At(Window, new Point(0.59, 0.59), Grip));
        Assert.Equal(WindowGrip.Corner, WindowGeometry.At(Window, new Point(0.21, 0.21), Grip));
        Assert.Equal(WindowGrip.None, WindowGeometry.At(Window, new Point(0.4, 0.4), Grip));
        Assert.Equal(WindowGrip.None, WindowGeometry.At(Window, new Point(0.9, 0.9), Grip));
    }

    [Fact]
    public void Moving_carries_the_window_and_keeps_its_size()
    {
        var moved = WindowGeometry.Moved(Window, new Point(0.4, 0.21), new Point(0.5, 0.31));

        Assert.Equal(0.3, moved.X, 6);
        Assert.Equal(0.3, moved.Y, 6);
        Assert.Equal(Window.Width, moved.Width, 6);
        Assert.Equal(Window.Height, moved.Height, 6);
    }

    /// <summary>The corner taken follows the hand; the opposite one stays.</summary>
    [Fact]
    public void Sizing_moves_the_corner_taken_and_keeps_the_opposite_one()
    {
        var bigger = WindowGeometry.Resized(Window, new Point(0.59, 0.59), new Point(0.8, 0.7));

        Assert.Equal(new Rect(0.2, 0.2, 0.6, 0.5), Rounded(bigger));

        var fromTopLeft = WindowGeometry.Resized(Window, new Point(0.21, 0.21), new Point(0.1, 0.15));

        Assert.Equal(new Rect(0.1, 0.15, 0.5, 0.45), Rounded(fromTopLeft));
    }

    /// <summary>Pulled past its opposite corner, a window stops at the smallest size rather than turning inside out.</summary>
    [Fact]
    public void A_window_does_not_turn_inside_out()
    {
        var crushed = WindowGeometry.Resized(Window, new Point(0.59, 0.59), new Point(0.0, 0.0));

        Assert.Equal(0.2, crushed.X, 6);
        Assert.Equal(WindowGeometry.Smallest, crushed.Width, 6);
        Assert.Equal(WindowGeometry.Smallest, crushed.Height, 6);
    }

    private static Rect Rounded(Rect rect) =>
        new(Math.Round(rect.X, 6), Math.Round(rect.Y, 6), Math.Round(rect.Width, 6), Math.Round(rect.Height, 6));
}
