using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

public sealed class PlacementTests
{
    private const double SmallScale = 0.2;

    /// <summary>Large enough that a 1080p screen runs out of slots inside a twenty-picture run.</summary>
    private const double CrowdedScale = 0.4;

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

    /// <summary>
    /// What the table found (hand-run of M2b, step 16): "flow rarely puts pictures side by side and
    /// usually straight on top of each other". Once the grid was full, the FIRST slot won every
    /// time, so everything from there on formed one growing stack. Now the slots start over.
    /// <para>
    /// The number of slots is not written down here - it is read off the run itself, at the point
    /// the positions begin to repeat - so the test measures the CYCLE and not the grid. The old
    /// behaviour passes the first comparison of the loop and fails the second, which is why the
    /// loop runs to the end rather than checking one position.
    /// </para>
    /// <para>
    /// The size is <see cref="CrowdedScale"/> and not the small one the other tests use, and that
    /// is the whole point: at <see cref="SmallScale"/> a 1080p screen holds 28 slots, so twenty
    /// pictures never fill the grid and the fallback never runs at all. The first version of this
    /// test did exactly that and stayed green with the OLD code in place - hence the assertion that
    /// the run overflowed, which is what makes the rest of it mean anything.
    /// </para>
    /// </summary>
    [Fact]
    public void A_full_grid_starts_over_from_the_first_slot()
    {
        var screen = Build.Screen();
        var scene = SceneState.Empty;
        var positions = new List<Point>();

        for (var i = 0; i < 20; i++)
        {
            var position = Placement.NextPosition(scene, CrowdedScale, Square, screen);
            positions.Add(position);
            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, CrowdedScale, Square)]);
        }

        var slots = positions.Distinct().Count();
        Assert.True(
            positions.Count > slots,
            $"the grid held {slots} slots and never filled, so nothing about the cycle was measured");

        for (var i = slots; i < positions.Count; i++)
        {
            Assert.Equal(positions[i % slots], positions[i]);
        }
    }

    /// <summary>
    /// The other half of step 16, and the reason the mode looked broken at all: at the size a
    /// picture used to arrive in - half the screen height - a 4:3 picture measures 0.375 × 0.5
    /// normalised, and the grid holds exactly TWO slots. Flow with two slots is a stack from the
    /// third picture on, whatever the fallback does.
    /// <para>
    /// Both numbers are written out rather than read from <c>ScreenContext</c>: reading the
    /// constant would assert that the code equals itself, and it is the SIX that was decided.
    /// </para>
    /// </summary>
    [Fact]
    public void The_size_a_picture_arrives_in_leaves_room_for_six()
    {
        var screen = Build.Screen();
        var scale = Layout.ScaleOnLoad(aspectRatio: 4d / 3d, screen);
        var scene = SceneState.Empty;
        var positions = new List<Point>();

        for (var i = 0; i < 12; i++)
        {
            var position = Placement.NextPosition(scene, scale, 4d / 3d, screen);
            positions.Add(position);
            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, scale, 4d / 3d)]);
        }

        Assert.Equal(6, positions.Distinct().Count());
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
    /// The row is exactly as tall as the picture, and this is what replaced the caption test
    /// (M2b). The Java version drew the name BELOW the image, which grew the row and let a
    /// captioned row overlap the one under it (commit 37e946c); ours draws it inside, so an item
    /// never reaches past its own rectangle.
    /// <para>
    /// Stated as an exact distance rather than as "no bigger than": a slack assertion would still
    /// hold if some allowance crept back in, and that allowance is precisely the thing that was
    /// removed.
    /// </para>
    /// </summary>
    [Fact]
    public void A_row_is_exactly_as_tall_as_the_picture()
    {
        var screen = Build.Screen();
        var scene = SceneState.Empty;

        var rows = new List<double>();

        for (var i = 0; i < ItemsPastOneRow; i++)
        {
            var position = Placement.NextPosition(scene, SmallScale, Square, screen);
            rows.Add(position.Y);
            scene = Build.SceneWith([.. scene.Items, Build.Item(position.X, position.Y, SmallScale, Square)]);
        }

        var distinct = rows.Distinct().OrderBy(y => y).ToList();
        Assert.True(distinct.Count > 1, "the run never reached a second row");

        // Both numbers are derived from what the placement itself produced, so the test carries no
        // second copy of the geometry: the height comes from the item's own rectangle, and the gap
        // is whatever margin the first row kept from the top edge.
        var height = Layout.ItemToRect(Build.Item(0.5, 0.5, SmallScale, Square), screen).Height;
        var gap = distinct[0] - (height / 2);

        Assert.Equal(height + gap, distinct[1] - distinct[0], precision: 9);
    }
}
