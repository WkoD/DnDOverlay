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

    /// <summary>
    /// <b>A drawn picture is turned once, not twice.</b> The mapped box says where a picture ends
    /// up; a renderer that then turns it by <see cref="Viewing.AngleInView"/> carries the view's
    /// angle with it, so what it must DRAW into is the box whose turn gives the mapped one.
    /// <para>
    /// Measured at the table before it was written down: at 90 degrees the pictures came out turned
    /// and squeezed into the other axis' box, while the selection outline over them - which is not
    /// turned - stayed right. That pair is the whole shape of the fault.
    /// </para>
    /// </summary>
    [Fact]
    public void The_box_a_picture_is_drawn_in_is_the_mapped_box_before_the_views_turn()
    {
        // Deliberately not square: a square box would be its own inverse and prove nothing.
        var mapped = new Rect(0.2, 0.1, 0.6, 0.2);

        var drawn = Viewing.BeforeTurn(mapped, ViewRotation.Quarter);

        Assert.Equal(mapped.Height, drawn.Width, 9);
        Assert.Equal(mapped.Width, drawn.Height, 9);

        // Same centre, so the turn about that centre lands exactly on the mapped box.
        Assert.Equal(mapped.X + (mapped.Width / 2), drawn.X + (drawn.Width / 2), 9);
        Assert.Equal(mapped.Y + (mapped.Height / 2), drawn.Y + (drawn.Height / 2), 9);

        // Undone again it is the box we started from. NOT compared against ToView: that maps a
        // rectangle from the SCENE into the view and moves the centre with it, while this turns a
        // box already in the view about its own centre. Two different operations that happen to
        // share the swap - asserting one against the other would be the sort of check that passes
        // for the wrong reason.
        var back = Viewing.BeforeTurn(drawn, ViewRotation.ThreeQuarters);

        Assert.Equal(mapped.X, back.X, 9);
        Assert.Equal(mapped.Y, back.Y, 9);
        Assert.Equal(mapped.Width, back.Width, 9);
        Assert.Equal(mapped.Height, back.Height, 9);
    }

    /// <summary>Nothing to undo where the view turns nothing.</summary>
    [Fact]
    public void An_upright_view_leaves_the_box_alone()
    {
        var mapped = new Rect(0.2, 0.1, 0.6, 0.2);

        Assert.Equal(mapped, Viewing.BeforeTurn(mapped, ViewRotation.None));
        Assert.Equal(mapped, Viewing.BeforeTurn(mapped, ViewRotation.Half));
    }

    /// <summary>
    /// <b>The direction, which is where this can go wrong silently.</b> The two quarter turns must
    /// undo the renderer's <c>+view</c> and therefore go OPPOSITE ways - a version that turned the
    /// same way for both would pass every half-turn check ever written (Guide <c>C14</c>).
    /// </summary>
    [Fact]
    public void The_two_quarter_turns_undo_the_view_in_opposite_directions()
    {
        var centre = new Point(0.5, 0.5);
        var right = new Point(0.9, 0.5);

        // +90 in a Y-down space is clockwise, so undoing it takes a point on the right upwards.
        var quarter = Viewing.BeforeTurn(right, centre, ViewRotation.Quarter);

        Assert.Equal(0.5, quarter.X, 9);
        Assert.Equal(0.1, quarter.Y, 9);

        // And the other way for three quarters.
        var three = Viewing.BeforeTurn(right, centre, ViewRotation.ThreeQuarters);

        Assert.Equal(0.5, three.X, 9);
        Assert.Equal(0.9, three.Y, 9);

        // A half turn is the symmetric case: it says nothing about which way either of them went.
        var half = Viewing.BeforeTurn(right, centre, ViewRotation.Half);

        Assert.Equal(0.1, half.X, 9);
        Assert.Equal(0.5, half.Y, 9);
    }

    /// <summary>
    /// Undoing the turn and applying it again is the picture standing where it started - the
    /// counter-check without which the two cases above could both be turning the wrong way by the
    /// same amount.
    /// </summary>
    [Theory]
    [InlineData(ViewRotation.None)]
    [InlineData(ViewRotation.Quarter)]
    [InlineData(ViewRotation.Half)]
    [InlineData(ViewRotation.ThreeQuarters)]
    public void Undoing_a_turn_and_doing_it_again_changes_nothing(ViewRotation view)
    {
        var centre = new Point(0.4, 0.6);
        var at = new Point(0.75, 0.2);

        var there = Viewing.BeforeTurn(at, centre, view);
        var back = Viewing.BeforeTurn(
            Viewing.BeforeTurn(there, centre, view is ViewRotation.Quarter
                ? ViewRotation.ThreeQuarters
                : view is ViewRotation.ThreeQuarters ? ViewRotation.Quarter : view),
            centre,
            ViewRotation.None);

        Assert.Equal(at.X, back.X, 9);
        Assert.Equal(at.Y, back.Y, 9);
    }
}
