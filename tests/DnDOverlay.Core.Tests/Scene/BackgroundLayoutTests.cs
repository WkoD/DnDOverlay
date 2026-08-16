using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The background layer's geometry. Two fits and no free scaling: <c>Cover</c> fills and crops,
/// <c>Contain</c> shows everything with a margin - and a panorama is unusable under <c>Cover</c>,
/// which is why the second one exists at all (Part 6).
/// <para>
/// The screen is <c>(0, 0, 1, 1)</c>, so a rectangle reaching past it is the crop rather than a
/// mistake. What must never happen is the other way round: an offset that leaves a gap at an edge.
/// </para>
/// </summary>
public sealed class BackgroundLayoutTests
{
    /// <summary>16:9, the table the plan measures against.</summary>
    private static readonly ScreenContext Screen = Build.Screen();

    private const double Panorama = 32d / 9d;
    private const double Portrait = 9d / 32d;
    private const double Precision = 9;

    /// <summary>
    /// Cover fills. Whichever axis is short becomes exactly the screen, and the other overhangs -
    /// never the reverse, or the table would show a strip of nothing.
    /// </summary>
    [Theory]
    [InlineData(Panorama)]
    [InlineData(Portrait)]
    [InlineData(16d / 9d)]
    public void Cover_leaves_no_edge_uncovered(double aspectRatio)
    {
        var rect = Layout.BackgroundRect(aspectRatio, BackgroundFit.Cover, 0, 0, Screen);

        Assert.True(rect.Width >= 1 - double.Epsilon, $"width {rect.Width} leaves a gap");
        Assert.True(rect.Height >= 1 - double.Epsilon, $"height {rect.Height} leaves a gap");
        Assert.True(rect.X <= 0 && rect.Y <= 0);
        Assert.True(rect.X + rect.Width >= 1 - 1e-9 && rect.Y + rect.Height >= 1 - 1e-9);
    }

    /// <summary>
    /// Contain shows everything. The picture fits inside the screen on both axes and is centred -
    /// the whole point for a panorama, which Cover would slice a middle strip out of.
    /// </summary>
    [Theory]
    [InlineData(Panorama)]
    [InlineData(Portrait)]
    public void Contain_shows_the_whole_picture(double aspectRatio)
    {
        var rect = Layout.BackgroundRect(aspectRatio, BackgroundFit.Contain, 0, 0, Screen);

        Assert.True(rect.Width <= 1 + 1e-9 && rect.Height <= 1 + 1e-9);
        Assert.True(rect.X >= -1e-9 && rect.Y >= -1e-9);
        Assert.True(rect.X + rect.Width <= 1 + 1e-9 && rect.Y + rect.Height <= 1 + 1e-9);
    }

    /// <summary>
    /// The numbers, not just the signs. A 32:9 panorama on a 16:9 screen is exactly twice as wide
    /// as the screen under Cover, and exactly half as tall under Contain - stated as figures so
    /// that a change of formula has to be a decision.
    /// </summary>
    [Fact]
    public void A_panorama_on_a_sixteen_by_nine_screen_has_the_expected_size()
    {
        var cover = Layout.BackgroundRect(Panorama, BackgroundFit.Cover, 0, 0, Screen);
        var contain = Layout.BackgroundRect(Panorama, BackgroundFit.Contain, 0, 0, Screen);

        Assert.Equal(2, cover.Width, Precision);
        Assert.Equal(1, cover.Height, Precision);
        Assert.Equal(-0.5, cover.X, Precision);

        Assert.Equal(1, contain.Width, Precision);
        Assert.Equal(0.5, contain.Height, Precision);
        Assert.Equal(0.25, contain.Y, Precision);
    }

    /// <summary>
    /// The offset picks which part of the crop is seen, and it moves ONLY inside it: at the ends
    /// the picture's edge sits exactly on the screen's, never past it and never short of it
    /// (Part 11).
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void The_offset_moves_only_inside_the_crop(double offset)
    {
        var rect = Layout.BackgroundRect(Panorama, BackgroundFit.Cover, offset, 0, Screen);

        Assert.True(rect.X <= 1e-9, $"a gap of {-rect.X} opened on the left");
        Assert.True(rect.X + rect.Width >= 1 - 1e-9, "a gap opened on the right");
    }

    /// <summary>The ends are the edges, exactly - the assertion the range test above cannot make.</summary>
    [Fact]
    public void The_ends_of_the_offset_are_the_edges_of_the_picture()
    {
        var left = Layout.BackgroundRect(Panorama, BackgroundFit.Cover, -1, 0, Screen);
        var right = Layout.BackgroundRect(Panorama, BackgroundFit.Cover, 1, 0, Screen);

        Assert.Equal(0, left.X, Precision);
        Assert.Equal(1, right.X + right.Width, Precision);
    }

    /// <summary>
    /// A value beyond the ends is held at them rather than refused. It arrives over the wire from a
    /// control that may be newer, and a background sliding off the screen is a worse answer than a
    /// clamped one (rule 7).
    /// </summary>
    [Fact]
    public void An_offset_past_the_end_is_held_at_the_end()
    {
        var far = Layout.BackgroundRect(Panorama, BackgroundFit.Cover, 5, 0, Screen);
        var end = Layout.BackgroundRect(Panorama, BackgroundFit.Cover, 1, 0, Screen);

        Assert.Equal(end, far);
    }

    /// <summary>
    /// Under Contain the offset does nothing, and that is the honest outcome rather than a special
    /// case: the whole picture is visible, so there is nothing to choose between.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Under_contain_the_offset_has_nothing_to_move(double offset)
    {
        Assert.Equal(
            Layout.BackgroundRect(Panorama, BackgroundFit.Contain, 0, 0, Screen),
            Layout.BackgroundRect(Panorama, BackgroundFit.Contain, offset, offset, Screen));
    }

    /// <summary>
    /// A picture of the screen's own shape is the same rectangle either way - the case where the
    /// two fits have to agree, and the one a formula with a sign error still gets wrong.
    /// </summary>
    [Fact]
    public void A_picture_shaped_like_the_screen_fills_it_under_both_fits()
    {
        var cover = Layout.BackgroundRect(Screen.AspectRatio, BackgroundFit.Cover, 0, 0, Screen);
        var contain = Layout.BackgroundRect(Screen.AspectRatio, BackgroundFit.Contain, 0, 0, Screen);

        Assert.Equal(new Rect(0, 0, 1, 1), cover);
        Assert.Equal(cover, contain);
    }

    /// <summary>
    /// Nothing to fit against - a picture whose shape is unknown fills the screen. An empty layer
    /// would be the alternative, and showing a picture beats showing nothing.
    /// </summary>
    [Fact]
    public void A_picture_without_a_shape_fills_the_screen()
    {
        Assert.Equal(
            new Rect(0, 0, 1, 1),
            Layout.BackgroundRect(0, BackgroundFit.Cover, 0, 0, Screen));
    }
}
