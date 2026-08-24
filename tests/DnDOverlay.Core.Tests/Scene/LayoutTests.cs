using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

public sealed class LayoutTests
{
    /// <summary>
    /// The shape has to survive the two aspect ratios. Normalised X is a fraction of the screen
    /// WIDTH while <c>Scale</c> is a fraction of its HEIGHT, so a rectangle that looks right on
    /// 16:9 can be plainly wrong on 21:9 - measured back in DIP, the item must keep its own
    /// aspect ratio on both.
    /// </summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1080)]
    [InlineData(1080, 1920)]
    public void An_item_keeps_its_aspect_ratio_on_every_screen(int width, int height)
    {
        var screen = Build.Screen(width, height);
        var item = Build.Item(scale: 0.5, aspectRatio: 4d / 3d);

        var rect = Layout.ItemToRect(item, screen);

        var renderedWidth = rect.Width * screen.WidthInDip;
        var renderedHeight = rect.Height * screen.HeightInDip;

        Assert.Equal(4d / 3d, renderedWidth / renderedHeight, precision: 9);
    }

    [Fact]
    public void An_item_sits_around_its_centre()
    {
        var item = Build.Item(centerX: 0.25, centerY: 0.75, scale: 0.4, aspectRatio: 1);

        var rect = Layout.ItemToRect(item, Build.Screen());

        Assert.Equal(0.25, rect.X + (rect.Width / 2), precision: 9);
        Assert.Equal(0.75, rect.Y + (rect.Height / 2), precision: 9);
    }

    /// <summary>At 45° the axis-parallel extent is a quite different thing, and the edge clamp measures against it.</summary>
    [Fact]
    public void The_hull_of_a_rotated_item_is_larger_than_the_item()
    {
        var upright = Build.Item(scale: 0.4, aspectRatio: 1, rotationDeg: 0);
        var tilted = upright with { RotationDeg = 45 };
        var screen = Build.Screen();

        var hull = Layout.ItemToHullRect(tilted, screen);

        Assert.True(hull.Width > Layout.ItemToRect(upright, screen).Width);
        Assert.Equal(0.5, hull.X + (hull.Width / 2), precision: 9);
    }

    /// <summary>
    /// The hull is the bounding box of the picture as it is actually drawn, and this is the test
    /// that says so - by working the four corners out independently and comparing.
    /// <para>
    /// <b>The test above passed while the hull was wrong</b>, which is why this one exists. It
    /// asked whether the hull was bigger than the item and whether it stayed centred; both hold for
    /// any wrong answer that is too big. It also used a square picture on a screen the helper makes,
    /// so a fault that only appears when one normalised unit across is not one down had nowhere to
    /// show itself.
    /// </para>
    /// <para>
    /// <b>Found at the table</b> (hand-run of M3, step 18a): the rotation was applied to normalised
    /// offsets directly, which on 16:9 at 46 degrees made the hull a third too wide and a quarter
    /// too short. The clamp lets the centre out by half a hull, so the surplus width was slack that
    /// did not exist - and a picture walked entirely off the screen while the clamp believed 96 DIP
    /// of it were showing.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(45)]
    [InlineData(46)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(200)]
    public void The_hull_is_exactly_the_box_around_the_turned_corners(double rotationDeg)
    {
        // A wide screen and a picture that is not square: on anything square the fault this guards
        // against cannot appear.
        var screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
        var item = Build.Item(scale: 0.5, aspectRatio: 1.5, rotationDeg: rotationDeg);

        var rect = Layout.ItemToRect(item, screen);
        var hull = Layout.ItemToHullRect(item, screen);

        // The corners, turned on the GLASS and brought back - the same detour the renderer and
        // Manipulation.Pivot make, and the one the hull has to agree with.
        var radians = rotationDeg * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var across = rect.Width / 2 * screen.AspectRatio;
        var down = rect.Height / 2;

        var xs = new List<double>();
        var ys = new List<double>();

        foreach (var (dx, dy) in new[] { (-across, -down), (across, -down), (across, down), (-across, down) })
        {
            xs.Add(item.CenterX + (((dx * cos) - (dy * sin)) / screen.AspectRatio));
            ys.Add(item.CenterY + (dx * sin) + (dy * cos));
        }

        Assert.Equal(xs.Min(), hull.X, precision: 9);
        Assert.Equal(xs.Max(), hull.X + hull.Width, precision: 9);
        Assert.Equal(ys.Min(), hull.Y, precision: 9);
        Assert.Equal(ys.Max(), hull.Y + hull.Height, precision: 9);
    }

    [Fact]
    public void An_unrotated_item_has_no_separate_hull()
    {
        var item = Build.Item(scale: 0.4);
        var screen = Build.Screen();

        Assert.Equal(Layout.ItemToRect(item, screen), Layout.ItemToHullRect(item, screen));
    }

    /// <summary>
    /// The blind spot from Part 3: a 5000×500 panorama at <c>ScaleOnLoad</c> 0.5 would arrive
    /// three times as wide as a 1080p table, of which a middle slice would be visible.
    /// </summary>
    [Fact]
    public void A_very_wide_image_arrives_capped_to_the_screen_width()
    {
        var screen = Build.Screen();

        var scale = Layout.ScaleOnLoad(aspectRatio: 10, screen);
        var rect = Layout.ItemToRect(Build.Item(scale: scale, aspectRatio: 10), screen);

        Assert.Equal(screen.MaxWidthOnLoad, rect.Width, precision: 9);
        Assert.True(scale < screen.ScaleOnLoad);
    }

    [Fact]
    public void A_portrait_image_is_not_capped()
    {
        var screen = Build.Screen();

        Assert.Equal(screen.ScaleOnLoad, Layout.ScaleOnLoad(aspectRatio: 0.5, screen), precision: 9);
    }

    /// <summary>
    /// "80 DIP on the shorter edge" (Part 6) - which edge that is depends on the item, so the
    /// bound cannot be a plain scale factor. A narrow image is the case that proves it.
    /// </summary>
    [Fact]
    public void Nothing_shrinks_below_a_touchable_size()
    {
        var screen = Build.Screen();
        const double NarrowAspect = 0.25;

        var scale = Layout.ClampScale(0.0001, NarrowAspect, screen);
        var rect = Layout.ItemToRect(Build.Item(scale: scale, aspectRatio: NarrowAspect), screen);

        var shorterEdgeInDip = Math.Min(rect.Width * screen.WidthInDip, rect.Height * screen.HeightInDip);

        Assert.Equal(80, shorterEdgeInDip, precision: 6);
    }

    [Fact]
    public void Nothing_grows_past_the_upper_bound()
    {
        var screen = Build.Screen();

        Assert.Equal(screen.MaxScale, Layout.ClampScale(1000, aspectRatio: 1, screen), precision: 9);
    }

    /// <summary>
    /// A picture always fits the screen it is put on, however extreme its shape. Found at the
    /// table (hand-run of M2b, step 15): a 6500x39 panorama arrived at <b>694 %</b> of the screen
    /// width and hung out over both edges.
    /// <para>
    /// The cause was two demands contradicting each other, with the wrong one winning: the width
    /// cap computed a scale of 0.0096 and <c>ClampScale</c> raised it back to <c>MinScale</c>, the
    /// lower bound that stops a picture becoming too small to grab. The cap wins now - a picture
    /// that does not fit is unusable for everyone, and the lower bound belongs to the gesture.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(6500, 39)]
    [InlineData(5000, 500)]
    [InlineData(10000, 100)]
    [InlineData(39, 6500)]
    public void A_picture_of_any_shape_arrives_inside_the_screen(double pixelWidth, double pixelHeight)
    {
        var screen = Build.Screen();
        var aspectRatio = pixelWidth / pixelHeight;

        var scale = Layout.ScaleOnLoad(aspectRatio, screen);
        var rect = Layout.ItemToRect(Build.Item(scale: scale, aspectRatio: aspectRatio), screen);

        Assert.True(
            rect.Width <= screen.MaxWidthOnLoad + 1e-9,
            $"{rect.Width * 100:F0} % of the screen width, capped at {screen.MaxWidthOnLoad * 100:F0} %");

        Assert.True(rect.Height <= 1 + 1e-9, $"{rect.Height * 100:F0} % of the screen height");
    }

    /// <summary>
    /// The counter-check that keeps the fix from being "cap everything to nothing": an ordinary
    /// picture is still laid out at the configured size, and the lower bound still holds for it.
    /// </summary>
    [Fact]
    public void An_ordinary_picture_is_untouched_by_the_cap()
    {
        var screen = Build.Screen();

        Assert.Equal(screen.ScaleOnLoad, Layout.ScaleOnLoad(4d / 3d, screen), precision: 9);
        Assert.Equal(screen.ScaleOnLoad, Layout.ScaleOnLoad(1, screen), precision: 9);
    }
}
