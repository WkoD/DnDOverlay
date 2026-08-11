using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

public sealed class PlacementTests
{
    private const double SmallScale = 0.2;
    private const double Square = 1;

    /// <summary>
    /// Seven of these fit across a 16:9 screen, so anything up to seven proves nothing about
    /// wrapping. The first version of this test used six and passed while measuring one row.
    /// </summary>
    private const int ItemsPastOneRow = 10;

    /// <summary>
    /// The reason placement lives in the hub at all: the paste hotkey, the inventory double tap
    /// and later a mobile device could otherwise work out the same slot and lay two images
    /// exactly on top of each other (Part 3).
    /// </summary>
    [Fact]
    public void Two_images_without_a_position_take_two_different_slots()
    {
        var screen = Build.Screen();

        var first = Placement.NextPosition(SceneState.Empty, SmallScale, Square, screen);
        var scene = Build.SceneWith(Build.Item(first.X, first.Y, SmallScale, Square));

        var second = Placement.NextPosition(scene, SmallScale, Square, screen);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The second correction to the ported <c>OlPane.addImage</c>: an image the players pushed
    /// out of its slot must not make the next one land on top of it.
    /// </summary>
    [Fact]
    public void An_occupied_slot_is_skipped()
    {
        var screen = Build.Screen();
        var first = Placement.NextPosition(SceneState.Empty, SmallScale, Square, screen);

        var scene = Build.SceneWith(Build.Item(first.X, first.Y, SmallScale, Square));
        var second = Placement.NextPosition(scene, SmallScale, Square, screen);

        var firstRect = Layout.ItemToRect(Build.Item(first.X, first.Y, SmallScale, Square), screen);
        var secondRect = Layout.ItemToRect(Build.Item(second.X, second.Y, SmallScale, Square), screen);

        var overlap = firstRect.Intersect(secondRect);

        Assert.True(overlap.Width <= 0 || overlap.Height <= 0);
    }

    /// <summary>Filling a whole row must step down, not run off the right edge.</summary>
    [Fact]
    public void A_full_row_wraps_to_the_next_one()
    {
        var screen = Build.Screen();
        var scene = SceneState.Empty;
        var positions = new List<Point>();

        for (var i = 0; i < ItemsPastOneRow; i++)
        {
            var position = Placement.NextPosition(scene, SmallScale, Square, screen);
            positions.Add(position);
            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, SmallScale, Square)]);
        }

        Assert.Contains(positions, p => p.Y > positions[0].Y);
        Assert.All(positions, p => Assert.InRange(p.X, 0, 1));
        Assert.All(positions, p => Assert.InRange(p.Y, 0, 1));
    }

    /// <summary>Everything stays on the screen, whatever the mode.</summary>
    [Theory]
    [InlineData(PlacementMode.Flow)]
    [InlineData(PlacementMode.Cascade)]
    public void Placement_stays_inside_the_screen(PlacementMode mode)
    {
        var screen = Build.Screen(placement: mode);
        var scene = SceneState.Empty;

        for (var i = 0; i < 10; i++)
        {
            var position = Placement.NextPosition(scene, SmallScale, Square, screen);

            Assert.InRange(position.X, 0, 1);
            Assert.InRange(position.Y, 0, 1);

            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, SmallScale, Square)]);
        }
    }

    /// <summary>Cascade is the deliberate opposite of flow: stacked with an offset, not side by side.</summary>
    [Fact]
    public void Cascade_offsets_each_image()
    {
        var screen = Build.Screen(placement: PlacementMode.Cascade);

        var first = Placement.NextPosition(SceneState.Empty, SmallScale, Square, screen);
        var scene = Build.SceneWith(Build.Item(first.X, first.Y, SmallScale, Square));
        var second = Placement.NextPosition(scene, SmallScale, Square, screen);

        Assert.NotEqual(first, second);
        Assert.True(second.X > first.X);
        Assert.True(second.Y > first.Y);
    }

    /// <summary>
    /// The caption is what wrecked the row height in the Java version (commit 37e946c). It is
    /// handed in rather than read from the screen context, so the effect must be visible here.
    /// </summary>
    [Fact]
    public void A_caption_pushes_the_next_row_further_down()
    {
        var screen = Build.Screen();
        var scene = SceneState.Empty;

        var withoutCaption = FirstSecondRowY(scene, screen, captionHeight: 0);
        var withCaption = FirstSecondRowY(scene, screen, captionHeight: 0.05);

        Assert.True(withCaption > withoutCaption);
    }

    private static double FirstSecondRowY(SceneState scene, ScreenContext screen, double captionHeight)
    {
        var positions = new List<Point>();

        for (var i = 0; i < ItemsPastOneRow; i++)
        {
            var position = Placement.NextPosition(scene, SmallScale, Square, screen, captionHeight);
            positions.Add(position);
            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, SmallScale, Square)]);
        }

        return positions.Select(p => p.Y).Distinct().OrderBy(y => y).Skip(1).First();
    }
}
