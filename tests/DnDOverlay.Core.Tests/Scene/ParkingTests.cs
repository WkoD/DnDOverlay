using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The slot bar. The promise being tested is a single sentence from Part 11 - "parked items always
/// lie in a slot that can be hit, even when there are more of them than places" - and it is the
/// one the players find out about on their own.
/// </summary>
public sealed class ParkingTests
{
    private static SceneState Parked(int count, ScreenContext screen) =>
        Parking.Arrange(
            Build.SceneWith([.. Enumerable.Range(0, count).Select(_ => Build.Item(parked: true))]),
            screen);

    /// <summary>How far a slot centre sits along its bar, whichever axis that bar runs on.</summary>
    private static double Along(SceneItem item, ScreenContext screen) =>
        screen.ParkEdge is ParkEdge.Left or ParkEdge.Right ? item.CenterY : item.CenterX;

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(20)]
    public void Every_parked_picture_lies_inside_the_bar(int count)
    {
        var screen = Build.Screen();

        foreach (var item in Parked(count, screen).Items)
        {
            Assert.InRange(Along(item, screen), 0.1, 0.9);
        }
    }

    /// <summary>
    /// The corners belong to Windows, so the bar stays out of them - and a parked picture keeps
    /// exactly the graspable remainder the edge clamp grants everything else.
    /// </summary>
    [Theory]
    [InlineData(ParkEdge.Left)]
    [InlineData(ParkEdge.Right)]
    [InlineData(ParkEdge.Top)]
    [InlineData(ParkEdge.Bottom)]
    public void A_parked_picture_sits_where_the_clamp_would_leave_it(ParkEdge edge)
    {
        var screen = Build.Screen() with { ParkEdge = edge };

        var parked = Parked(3, screen).Items[1];

        Assert.Equal(parked, Manipulation.HoldAtEdge(parked, screen));
    }

    /// <summary>
    /// Overcrowding overlaps them and does not shrink them: every one keeps a slot's worth of
    /// itself, so there is no state in which something is parked and out of reach (Part 6).
    /// </summary>
    [Fact]
    public void More_pictures_than_places_overlap_rather_than_shrink()
    {
        var screen = Build.Screen();
        var capacity = Parking.Capacity(screen);

        var crowded = Parked(capacity * 3, screen).Items;

        var steps = crowded
            .Zip(crowded.Skip(1), (first, second) => Along(second, screen) - Along(first, screen))
            .ToList();

        Assert.All(steps, step => Assert.True(step > 0, "two parked pictures share a place"));
        Assert.True(steps[0] < screen.MinVisibleNormalisedY, "the bar was not crowded at all");

        // The slot itself did not get smaller: across the bar each of them still shows its full
        // remainder, and that is what "hittable" means.
        Assert.All(crowded, item => Assert.Equal(item, Manipulation.HoldAtEdge(item, screen)));
    }

    [Fact]
    public void A_short_bar_sits_in_the_middle_of_its_edge()
    {
        var screen = Build.Screen();

        var two = Parked(2, screen).Items;

        Assert.Equal(0.5, (Along(two[0], screen) + Along(two[1], screen)) / 2, precision: 9);
    }

    /// <summary>
    /// The order is the scene's order, and it survives - a picture does not jump to the other end
    /// of the bar because somebody parked another one.
    /// </summary>
    [Fact]
    public void The_bar_keeps_its_order_and_closes_up_when_one_leaves()
    {
        var screen = Build.Screen();
        var scene = Parked(4, screen);

        var second = scene.Items[1];
        var shorter = Parking.Arrange(
            scene with { Items = [.. scene.Items.Where(item => item.ItemId != scene.Items[0].ItemId)] },
            screen);

        Assert.Equal([.. scene.Items.Skip(1).Select(item => item.ItemId)], [.. shorter.Items.Select(item => item.ItemId)]);
        Assert.NotEqual(Along(second, screen), Along(shorter.Items[0], screen));
    }

    [Fact]
    public void Pictures_that_are_not_parked_are_not_touched()
    {
        var screen = Build.Screen();
        var lying = Build.Item(centerX: 0.2, centerY: 0.3);

        var scene = Parking.Arrange(Build.SceneWith(lying, Build.Item(parked: true)), screen);

        Assert.Equal(lying, scene.Items[0]);
    }

    /// <summary>
    /// Changing the edge during play moves the whole bar, because the positions are computed from
    /// the list rather than stored - at a table "right" is left from the other side (Part 6).
    /// </summary>
    [Fact]
    public void Changing_the_park_edge_moves_the_whole_bar()
    {
        var screen = Build.Screen();
        var scene = Parked(3, screen);

        var moved = Parking.Arrange(scene, screen with { ParkEdge = ParkEdge.Left });

        Assert.All(scene.Items, item => Assert.True(item.CenterX > 0.5));
        Assert.All(moved.Items, item => Assert.True(item.CenterX < 0.5));
    }

    /// <summary>Nine at 96 DIP along a 1080-DIP edge - the number the parameter table produces.</summary>
    [Fact]
    public void A_1080p_table_holds_nine_parked_pictures_side_by_side()
    {
        Assert.Equal(9, Parking.Capacity(Build.Screen()));
    }
}
