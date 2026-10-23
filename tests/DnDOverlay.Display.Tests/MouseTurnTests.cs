using System.Windows;

namespace DnDOverlay.Display.Tests;

/// <summary>Ctrl+drag turns a picture about its centre by the angle the mouse swept (Part 6).</summary>
public sealed class MouseTurnTests
{
    private static readonly Point Centre = new(500, 300);

    /// <summary>From the right of the centre to below it is a quarter turn clockwise on a screen whose y grows downwards.</summary>
    [Fact]
    public void A_quarter_round_below_is_ninety_degrees_clockwise()
    {
        Assert.Equal(90, MouseTurn.Swept(Centre, new Point(600, 300), new Point(500, 400)), 6);
    }

    [Fact]
    public void Back_the_other_way_is_counter_clockwise()
    {
        Assert.Equal(-90, MouseTurn.Swept(Centre, new Point(500, 400), new Point(600, 300)), 6);
    }

    /// <summary>Only the angle counts: moving straight away from the centre turns nothing.</summary>
    [Fact]
    public void Moving_away_from_the_centre_turns_nothing()
    {
        Assert.Equal(0, MouseTurn.Swept(Centre, new Point(600, 300), new Point(900, 300)), 6);
    }

    /// <summary>
    /// Crossing the line left of the centre - where the angle jumps from +180 to -180 - is a small step
    /// like any other. Read as -358 degrees it left the dead zone at once, and a picture meant to stay
    /// straight jumped by the whole dead zone (Part 6, <c>Manipulation.Turn</c>).
    /// </summary>
    [Fact]
    public void Crossing_the_left_of_the_centre_is_a_small_step()
    {
        var down = MouseTurn.Swept(Centre, new Point(400, 299), new Point(400, 301));
        var up = MouseTurn.Swept(Centre, new Point(400, 301), new Point(400, 299));

        Assert.InRange(down, -1.2, -1.1);
        Assert.InRange(up, 1.1, 1.2);
    }

    /// <summary>The same angle near the centre and far from it.</summary>
    [Fact]
    public void The_distance_from_the_centre_does_not_matter()
    {
        var near = MouseTurn.Swept(Centre, new Point(510, 300), new Point(510, 310));
        var far = MouseTurn.Swept(Centre, new Point(800, 300), new Point(800, 600));

        Assert.Equal(45, near, 6);
        Assert.Equal(45, far, 6);
    }
}
