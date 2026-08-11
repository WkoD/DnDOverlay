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
}
