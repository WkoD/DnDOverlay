using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The view rotation, both ways. It is the second rotation formula this project has written, and
/// the first one was wrong for a while with 935 tests standing around it - the fault sat on an
/// axis the test data never distinguished (Guide C14).
/// <para>
/// So nothing here is symmetric: the points are off-centre on both axes, the rectangles are not
/// square, and every quarter turn is asked separately. <b>180 degrees in particular proves the
/// least</b> of the four, and it is the one the milestone's own sign-off sentence uses.
/// </para>
/// </summary>
public sealed class ViewingTests
{
    private const double Precision = 12;

    /// <summary>
    /// A quarter turn clockwise: the left edge of the table becomes the top edge of the view. Read
    /// on a point that is near neither axis, so a swapped pair cannot pass.
    /// </summary>
    [Fact]
    public void A_quarter_turn_puts_the_left_edge_at_the_top()
    {
        var seen = Viewing.ToView(new Point(0.2, 0.7), ViewRotation.Quarter);

        Assert.Equal(0.3, seen.X, Precision);
        Assert.Equal(0.2, seen.Y, Precision);
    }

    /// <summary>Three quarters is the other one, and it is not the same as a quarter backwards.</summary>
    [Fact]
    public void Three_quarters_is_not_a_quarter_the_other_way()
    {
        var quarter = Viewing.ToView(new Point(0.2, 0.7), ViewRotation.Quarter);
        var three = Viewing.ToView(new Point(0.2, 0.7), ViewRotation.ThreeQuarters);

        Assert.Equal(0.7, three.X, Precision);
        Assert.Equal(0.8, three.Y, Precision);
        Assert.NotEqual(quarter, three);
    }

    /// <summary>
    /// There and back for all four turns. The property that matters is not any single number but
    /// that the pair is a pair - a hit test uses the inverse of what the drawing used, and if they
    /// disagree the DM grabs one picture and moves another.
    /// </summary>
    [Theory]
    [InlineData(ViewRotation.None)]
    [InlineData(ViewRotation.Quarter)]
    [InlineData(ViewRotation.Half)]
    [InlineData(ViewRotation.ThreeQuarters)]
    public void Every_turn_is_undone_by_its_inverse(ViewRotation view)
    {
        var scene = new Point(0.13, 0.82);

        var there = Viewing.ToView(scene, view);
        var back = Viewing.ToScene(there, view);

        Assert.Equal(scene.X, back.X, Precision);
        Assert.Equal(scene.Y, back.Y, Precision);
    }

    /// <summary>
    /// The sentence M4 is signed off against: "a drag to the right moves the picture at the table
    /// to the LEFT" - in a view turned by 180 degrees (Part 10).
    /// </summary>
    [Fact]
    public void A_drag_to_the_right_moves_the_picture_left_in_a_turned_view()
    {
        var moved = Viewing.DeltaToScene(new Point(0.1, 0), ViewRotation.Half);

        Assert.Equal(-0.1, moved.X, Precision);
        Assert.Equal(0, moved.Y, Precision);
    }

    /// <summary>
    /// At a quarter turn the same drag moves the picture along the OTHER axis, and the sign is the
    /// half that a symmetric test would let through: dragging right in the view is dragging up at
    /// the table, not down.
    /// </summary>
    [Fact]
    public void A_drag_to_the_right_moves_the_picture_up_at_a_quarter_turn()
    {
        var moved = Viewing.DeltaToScene(new Point(0.1, 0), ViewRotation.Quarter);

        Assert.Equal(0, moved.X, Precision);
        Assert.Equal(-0.1, moved.Y, Precision);
    }

    /// <summary>A movement, turned and turned back, is the movement again - all four turns.</summary>
    [Theory]
    [InlineData(ViewRotation.None)]
    [InlineData(ViewRotation.Quarter)]
    [InlineData(ViewRotation.Half)]
    [InlineData(ViewRotation.ThreeQuarters)]
    public void A_delta_survives_both_directions(ViewRotation view)
    {
        var delta = new Point(0.07, -0.03);

        var scene = Viewing.DeltaToScene(delta, view);

        // The way back is the drawing direction applied to a vector: turn it once more and the
        // three remaining quarters bring it home.
        var home = Viewing.DeltaToScene(
            Viewing.DeltaToScene(Viewing.DeltaToScene(scene, view), view), view);

        Assert.Equal(delta.X, home.X, Precision);
        Assert.Equal(delta.Y, home.Y, Precision);
    }

    /// <summary>
    /// An angle turns with the view and comes back into 0..360, so that two angles meaning the same
    /// thing are the same number - a picture at 350 degrees in a view turned by 90 is at 80, not
    /// at 440.
    /// </summary>
    [Theory]
    [InlineData(0, ViewRotation.Quarter, 90)]
    [InlineData(350, ViewRotation.Quarter, 80)]
    [InlineData(45, ViewRotation.Half, 225)]
    [InlineData(300, ViewRotation.ThreeQuarters, 210)]
    public void An_angle_turns_with_the_view(double angle, ViewRotation view, double expected)
    {
        Assert.Equal(expected, Viewing.AngleInView(angle, view), Precision);
        Assert.Equal(angle, Viewing.AngleToScene(Viewing.AngleInView(angle, view), view), Precision);
    }

    /// <summary>
    /// A rectangle turns as a whole: its centre moves and, on a quarter turn, its two extents
    /// change places. Asked of a rectangle that is neither square nor centred, because either of
    /// those would survive half the mistakes one can make here.
    /// </summary>
    [Fact]
    public void A_rectangle_turns_around_the_screen_and_swaps_its_extents()
    {
        var scene = new Rect(0.1, 0.2, 0.4, 0.2);

        var quarter = Viewing.ToView(scene, ViewRotation.Quarter);

        // Centre (0.3, 0.3) turns to (0.7, 0.3); the extents change places.
        Assert.Equal(0.2, quarter.Width, Precision);
        Assert.Equal(0.4, quarter.Height, Precision);
        Assert.Equal(0.6, quarter.X, Precision);
        Assert.Equal(0.1, quarter.Y, Precision);

        var half = Viewing.ToView(scene, ViewRotation.Half);

        Assert.Equal(scene.Width, half.Width, Precision);
        Assert.Equal(scene.Height, half.Height, Precision);
        Assert.Equal(0.5, half.X, Precision);
        Assert.Equal(0.6, half.Y, Precision);
    }

    /// <summary>
    /// The other half of a quarter turn, and the one that is invisible until it is missing: the
    /// tile has to be the turned shape, or everything drawn inside it is stretched.
    /// </summary>
    [Fact]
    public void A_quarter_turn_turns_the_shape_of_the_view_as_well()
    {
        Assert.Equal(9d / 16d, Viewing.AspectRatioInView(16d / 9d, ViewRotation.Quarter), Precision);
        Assert.Equal(16d / 9d, Viewing.AspectRatioInView(16d / 9d, ViewRotation.Half), Precision);
        Assert.Equal(9d / 16d, Viewing.AspectRatioInView(16d / 9d, ViewRotation.ThreeQuarters), Precision);

        // Nothing to turn is not an error here either: a shape of zero stays what it was, as it
        // does everywhere else in Layout.
        Assert.Equal(0, Viewing.AspectRatioInView(0, ViewRotation.Quarter), Precision);
    }
}
