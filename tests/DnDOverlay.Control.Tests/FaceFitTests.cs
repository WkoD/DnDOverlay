using System.Windows;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// Where the face of a tile lies in its room (Part 7): the table's shape, centred, the height leading in
/// the overview - and a grip measured against the same rectangle the layers are laid on (rule 9).
/// </summary>
public sealed class FaceFitTests
{
    /// <summary>In the overview the height is fixed whatever room there is: rows of unequal height leave holes.</summary>
    [Fact]
    public void In_the_overview_the_height_leads()
    {
        var tile = new Size(FaceFit.Small * (16.0 / 9), FaceFit.Small);

        Assert.Equal(tile, FaceFit.Wanted(new Size(2000, 900), 16.0 / 9, opened: false));
        Assert.Equal(tile, FaceFit.Wanted(new Size(double.PositiveInfinity, double.PositiveInfinity), 16.0 / 9, opened: false));
    }

    /// <summary>A window narrower than one tile gets a narrower face of the same shape, never a wider one.</summary>
    [Fact]
    public void Narrower_than_a_tile_the_face_shrinks_in_its_shape() =>
        Assert.Equal(new Size(160, 90), FaceFit.Wanted(new Size(160, 900), 16.0 / 9, opened: false));

    /// <summary>An open tile takes the room it is given, in the table's shape; offered infinite height, it falls back to the overview's.</summary>
    [Fact]
    public void An_open_tile_takes_the_room_in_the_tables_shape()
    {
        Assert.Equal(new Size(1600, 900), FaceFit.Wanted(new Size(2000, 900), 16.0 / 9, opened: true));
        Assert.Equal(new Size(900, 506.25), FaceFit.Wanted(new Size(900, 900), 16.0 / 9, opened: true));
        Assert.Equal(FaceFit.Small, FaceFit.Wanted(new Size(2000, double.PositiveInfinity), 16.0 / 9, opened: true).Height);
    }

    /// <summary>A table seen from its short side stands upright in the tile.</summary>
    [Fact]
    public void A_table_seen_from_its_short_side_stands_upright() =>
        Assert.Equal(new Size(506.25, 900), FaceFit.Wanted(new Size(2000, 900), 9.0 / 16, opened: true));

    [Fact]
    public void A_table_without_a_shape_has_no_face() =>
        Assert.Equal(new Size(0, 0), FaceFit.Wanted(new Size(2000, 900), 0, opened: true));

    /// <summary>
    /// The face is centred, and a grip subtracts exactly that margin: a point on the panel's middle is the
    /// face's middle (the menu far left of the pointer, hand-run of M4, 24a).
    /// </summary>
    [Fact]
    public void A_grip_subtracts_the_centring_the_layers_were_laid_with()
    {
        var panel = new Size(2000, 900);
        var face = new Size(1600, 900);

        Assert.Equal(new Point(200, 0), FaceFit.Corner(panel, face));
        Assert.Equal(new Point(800, 450), FaceFit.OnFace(new Point(1000, 450), panel, face));
    }

    /// <summary>A face larger than its panel - mid-layout - is not pushed off the top or left edge.</summary>
    [Fact]
    public void A_face_larger_than_its_panel_stays_at_the_corner() =>
        Assert.Equal(new Point(0, 0), FaceFit.Corner(new Size(100, 50), new Size(267, 150)));

    [Fact]
    public void The_edges_belong_to_the_face_and_the_margin_does_not()
    {
        var face = new Size(1600, 900);

        Assert.True(FaceFit.Within(new Point(0, 0), face));
        Assert.True(FaceFit.Within(new Point(1600, 900), face));
        Assert.False(FaceFit.Within(new Point(-0.5, 400), face));
        Assert.False(FaceFit.Within(new Point(800, 900.5), face));
    }

    /// <summary>A frame dragged out of the tile stops at its edge (Part 3: a selection does not cross screens).</summary>
    [Fact]
    public void A_frame_stops_at_the_edge_of_the_face()
    {
        var face = new Size(1600, 900);

        Assert.Equal(new Point(0, 900), FaceFit.Clamped(new Point(-40, 1200), face));
        Assert.Equal(new Point(1600, 300), FaceFit.Clamped(new Point(1700, 300), face));
        Assert.Equal(new Point(0, 0), FaceFit.Clamped(new Point(50, 50), new Size(0, 0)));
    }
}
