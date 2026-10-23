using System.Windows;

namespace DnDOverlay.Control.Tests;

/// <summary>Ctrl+drag turns about the centre (Part 6, Part 7): the angle the mouse swept, one step at a time.</summary>
public sealed class MouseTurnTests
{
    private static readonly Point Centre = new(100, 100);

    /// <summary>A quarter of the way round, clockwise on screen, is +90 degrees.</summary>
    [Fact]
    public void A_quarter_round_clockwise_is_ninety_degrees() =>
        Assert.Equal(90, MouseTurn.Swept(Centre, new Point(150, 100), new Point(100, 150)), 9);

    [Fact]
    public void Back_the_other_way_is_negative() =>
        Assert.Equal(-90, MouseTurn.Swept(Centre, new Point(100, 150), new Point(150, 100)), 9);

    /// <summary>
    /// A small step across the line left of the centre - where the angle jumps from +180 to -180 - is a
    /// small step, not nearly a whole turn the other way: the picture jumped by about 358 degrees and the
    /// rotation dead zone was blown through.
    /// </summary>
    [Fact]
    public void Crossing_the_left_of_the_centre_is_a_small_step()
    {
        var above = new Point(50, 99);
        var below = new Point(50, 101);

        var down = MouseTurn.Swept(Centre, above, below);
        var up = MouseTurn.Swept(Centre, below, above);

        Assert.InRange(down, -3, 0);
        Assert.InRange(up, 0, 3);
        Assert.Equal(-down, up, 9);
    }

    [Fact]
    public void Standing_still_is_no_turn() =>
        Assert.Equal(0, MouseTurn.Swept(Centre, new Point(40, 100), new Point(40, 100)), 9);
}
