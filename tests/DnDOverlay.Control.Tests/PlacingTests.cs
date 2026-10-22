using System.Windows;
using DnDOverlay.Core;
using CorePoint = DnDOverlay.Core.Point;
using CoreRect = DnDOverlay.Core.Rect;
using TilePoint = System.Windows.Point;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The one way between a scene and a tile (Part 1, rule 9): drawing and hitting are one calculation,
/// so a finger lands where the picture is drawn - at every quarter turn, not only the symmetric one.
/// </summary>
public sealed class PlacingTests
{
    /// <summary>A tile that is not square, so a mix-up of the two axes cannot hide.</summary>
    private static readonly Size Face = new(300, 150);

    public static TheoryData<ViewRotation> Views => [ViewRotation.None, ViewRotation.Quarter, ViewRotation.Half, ViewRotation.ThreeQuarters];

    /// <summary>
    /// A finger on the place a point is drawn finds that point again - the inverse, at every view.
    /// 180 degrees is the symmetric case and proves nothing about the sign (Guide C14); 90 and 270 do.
    /// </summary>
    [Theory]
    [MemberData(nameof(Views))]
    public void A_finger_on_a_drawn_point_finds_that_point(ViewRotation view)
    {
        var scene = new CorePoint(0.2, 0.7);

        var drawn = Placing.InTile(scene, view, Face);
        var found = Placing.InScene(drawn, view, Face);

        Assert.Equal(scene.X, found.X, 9);
        Assert.Equal(scene.Y, found.Y, 9);
    }

    /// <summary>A rectangle and its corner point are drawn in the same place - the marks sit on their picture.</summary>
    [Theory]
    [MemberData(nameof(Views))]
    public void A_rectangle_is_drawn_where_its_corners_are(ViewRotation view)
    {
        var rect = new CoreRect(0.1, 0.2, 0.3, 0.4);

        var drawn = Placing.InTile(rect, view, Face);
        var a = Placing.InTile(new CorePoint(rect.X, rect.Y), view, Face);
        var b = Placing.InTile(new CorePoint(rect.X + rect.Width, rect.Y + rect.Height), view, Face);

        Assert.Equal(Math.Min(a.X, b.X), drawn.X, 9);
        Assert.Equal(Math.Min(a.Y, b.Y), drawn.Y, 9);
        Assert.Equal(Math.Abs(b.X - a.X), drawn.Width, 9);
        Assert.Equal(Math.Abs(b.Y - a.Y), drawn.Height, 9);
    }

    /// <summary>
    /// A movement is not <see cref="Placing.InScene"/> of two points minus each other's translation by
    /// accident: it is the difference of the two places a finger hit, at every view.
    /// </summary>
    [Theory]
    [MemberData(nameof(Views))]
    public void A_movement_is_the_difference_between_the_two_places_hit(ViewRotation view)
    {
        var from = new TilePoint(40, 30);
        var to = new TilePoint(100, 110);

        var moved = Placing.DeltaInScene(new Vector(to.X - from.X, to.Y - from.Y), view, Face);
        var a = Placing.InScene(from, view, Face);
        var b = Placing.InScene(to, view, Face);

        Assert.Equal(b.X - a.X, moved.X, 9);
        Assert.Equal(b.Y - a.Y, moved.Y, 9);
    }

    /// <summary>A tile with no size yet - before its first layout pass - hits nothing rather than dividing by zero.</summary>
    [Fact]
    public void A_tile_without_size_hits_the_origin_and_moves_nothing()
    {
        Assert.Equal(default, Placing.InScene(new TilePoint(10, 10), ViewRotation.Quarter, new Size(0, 150)));
        Assert.Equal(default, Placing.DeltaInScene(new Vector(10, 10), ViewRotation.Quarter, new Size(300, 0)));
    }

    /// <summary>Unturned, the box a picture is drawn into is the mapped box itself.</summary>
    [Fact]
    public void Without_a_view_the_box_before_the_turn_is_the_mapped_box()
    {
        var mapped = new System.Windows.Rect(10, 20, 60, 30);

        Assert.Equal(mapped, Placing.BeforeTurn(mapped, ViewRotation.None));
    }

    /// <summary>
    /// A quarter turn puts the extents back about the same centre: drawn into the mapped box and
    /// turned again, the picture was squeezed into the other axis (Handlauf M4, third run).
    /// </summary>
    [Fact]
    public void A_quarter_turn_swaps_the_extents_about_the_same_centre()
    {
        var mapped = new System.Windows.Rect(10, 20, 60, 30);

        var before = Placing.BeforeTurn(mapped, ViewRotation.Quarter);

        Assert.Equal(30, before.Width, 9);
        Assert.Equal(60, before.Height, 9);
        Assert.Equal(mapped.X + (mapped.Width / 2), before.X + (before.Width / 2), 9);
        Assert.Equal(mapped.Y + (mapped.Height / 2), before.Y + (before.Height / 2), 9);
    }
}
