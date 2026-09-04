using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The background layer's geometry. Since M4 it is the SAME geometry as an item's - a centre, a
/// scale, an angle - and the two fits are what put it into one of the two obvious positions:
/// <c>Cover</c> fills and crops, <c>Contain</c> shows everything with a margin. A panorama is
/// unusable under <c>Cover</c>, which is why the second one exists at all (Part 6).
/// <para>
/// The screen is <c>(0, 0, 1, 1)</c>, so a rectangle reaching past it is the crop rather than a
/// mistake.
/// </para>
/// <para>
/// <b>The offset tests are gone with the offset.</b> They asked whether a value could open a gap at
/// an edge, and the answer was a clamp inside the crop. What the offset expressed - which part of
/// the crop is seen - is now expressed by moving the picture, which can say more; whether the DM
/// may then leave a gap is a question about the GRIPS and is decided with them in M4c. What is
/// tested here is what the buttons produce.
/// </para>
/// </summary>
public sealed class BackgroundLayoutTests
{
    /// <summary>16:9, the table the plan measures against.</summary>
    private static readonly ScreenContext Screen = Build.Screen();

    /// <summary>32:9 - the shape both fits have to disagree about.</summary>
    private const double Panorama = 32d / 9d;

    private const double Precision = 9;

    /// <summary>
    /// Cover fills. Whichever axis is short becomes exactly the screen, and the other overhangs -
    /// never the reverse, or the table would show a strip of nothing.
    /// </summary>
    [Theory]
    [InlineData(3200, 900)]
    [InlineData(900, 3200)]
    [InlineData(1600, 900)]
    public void Cover_leaves_no_edge_uncovered(int width, int height)
    {
        var rect = Fitted(width, height, BackgroundFit.Cover);

        Assert.True(rect.Width >= 1 - double.Epsilon, $"width {rect.Width} leaves a gap");
        Assert.True(rect.Height >= 1 - double.Epsilon, $"height {rect.Height} leaves a gap");
        Assert.True(rect.X <= 1e-9 && rect.Y <= 1e-9);
        Assert.True(rect.X + rect.Width >= 1 - 1e-9 && rect.Y + rect.Height >= 1 - 1e-9);
    }

    /// <summary>
    /// Contain shows everything. The picture fits inside the screen on both axes and is centred -
    /// the whole point for a panorama, which Cover would slice a middle strip out of.
    /// </summary>
    [Theory]
    [InlineData(3200, 900)]
    [InlineData(900, 3200)]
    public void Contain_shows_the_whole_picture(int width, int height)
    {
        var rect = Fitted(width, height, BackgroundFit.Contain);

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
        var cover = Fitted(3200, 900, BackgroundFit.Cover);
        var contain = Fitted(3200, 900, BackgroundFit.Contain);

        Assert.Equal(2, cover.Width, Precision);
        Assert.Equal(1, cover.Height, Precision);
        Assert.Equal(-0.5, cover.X, Precision);

        Assert.Equal(1, contain.Width, Precision);
        Assert.Equal(0.5, contain.Height, Precision);
        Assert.Equal(0.25, contain.Y, Precision);
    }

    /// <summary>
    /// What the whole change is for: the background and an item of the same numbers occupy the same
    /// rectangle, because one formula computes both (rule 9). Written against a PANORAMA and a
    /// scale that is nobody's default - on a picture shaped like the screen two different formulas
    /// would agree as well (Guide C14).
    /// </summary>
    [Fact]
    public void A_background_and_an_item_of_the_same_numbers_occupy_the_same_rectangle()
    {
        var background = Build.Background(meta: Build.Meta(3200, 900), centerX: 0.4, centerY: 0.65, scale: 0.7);

        var item = Build.Item() with
        {
            CenterX = 0.4,
            CenterY = 0.65,
            Scale = 0.7,
            AspectRatio = Panorama,
        };

        Assert.Equal(Layout.ItemToRect(item, Screen), Layout.BackgroundRect(background, Screen));
    }

    /// <summary>
    /// Moving it moves it - the plainest consequence of the change, and the one that replaces the
    /// offset. Half a screen to the right is half a screen to the right, on both axes.
    /// </summary>
    [Fact]
    public void Moving_the_background_moves_its_rectangle()
    {
        var centred = Build.Background(centerX: 0.5, centerY: 0.5, scale: 1);
        var moved = centred with { CenterX = 1.0, CenterY = 0.25 };

        var before = Layout.BackgroundRect(centred, Screen);
        var after = Layout.BackgroundRect(moved, Screen);

        Assert.Equal(before.X + 0.5, after.X, Precision);
        Assert.Equal(before.Y - 0.25, after.Y, Precision);
        Assert.Equal(before.Width, after.Width, Precision);
        Assert.Equal(before.Height, after.Height, Precision);
    }

    /// <summary>
    /// A picture of the screen's own shape is the same rectangle either way - the case where the
    /// two fits have to agree, and the one a formula with a sign error still gets wrong.
    /// </summary>
    [Fact]
    public void A_picture_shaped_like_the_screen_fills_it_under_both_fits()
    {
        var cover = Fitted(1600, 900, BackgroundFit.Cover);
        var contain = Fitted(1600, 900, BackgroundFit.Contain);

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
        var shapeless = Build.Background(meta: Build.Meta(0, 0));

        Assert.Equal(new Rect(0, 0, 1, 1), Layout.BackgroundRect(shapeless, Screen));
    }

    /// <summary>
    /// The rectangle the button produces: fit first, then draw what it decided.
    /// <para>
    /// It takes pixels rather than a ratio, and that is not decoration. The fit is computed from
    /// the shape the METADATA gives and drawn from the same source, so a test that passes a ratio
    /// and builds metadata from it puts a rounding error between the two halves - measured at
    /// 0.09 % on a 9:32 portrait, which is exactly enough to open a gap that is not there.
    /// </para>
    /// </summary>
    private static Rect Fitted(int width, int height, BackgroundFit fit)
    {
        var meta = Build.Meta(width, height);
        var (centre, scale) = Layout.FitBackground(meta.AspectRatio, fit, Screen);

        return Layout.BackgroundRect(
            Build.Background(meta: meta, centerX: centre.X, centerY: centre.Y, scale: scale),
            Screen);
    }
}
