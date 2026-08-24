using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The three operations a gesture produces, as the reducer sees them. Both ends run this same
/// function, so what is tested here is what the table and the hub agree on (Part 1, rule 2).
/// </summary>
public sealed class SceneGestureTests
{
    private static readonly ScreenContext Screen = Build.Screen();

    [Fact]
    public void A_transform_moves_exactly_the_item_it_names()
    {
        var moved = Build.Item(centerX: 0.2);
        var other = Build.Item(centerX: 0.8);

        var after = SceneReducer.Apply(
            Build.SceneWith(moved, other),
            new TransformItem(moved.ItemId, 0.4, 0.6, 0.35, 90, ZOrder: 7, Revision: 42),
            Screen);

        var result = after.Items[0];

        Assert.Equal(0.4, result.CenterX);
        Assert.Equal(0.6, result.CenterY);
        Assert.Equal(0.35, result.Scale);
        Assert.Equal(90, result.RotationDeg);
        Assert.Equal(7, result.ZOrder);
        Assert.Equal(42, result.Revision);
        Assert.Equal(other, after.Items[1]);
    }

    /// <summary>
    /// The lock guards against the table, not against the DM: a command from the control goes
    /// through, and the refusal for one that came from a display sits in <c>ISessionApi</c>, where
    /// the origin is known (Part 3).
    /// </summary>
    [Fact]
    public void A_locked_item_is_still_moved_by_the_reducer()
    {
        var locked = Build.Item(locked: true);

        var after = SceneReducer.Apply(
            Build.SceneWith(locked),
            new TransformItem(locked.ItemId, 0.9, 0.9, 0.2, 0, ZOrder: 1, Revision: 2),
            Screen);

        Assert.Equal(0.9, after.Items[0].CenterX);
        Assert.True(after.Items[0].Locked);
    }

    [Fact]
    public void A_transform_for_an_unknown_item_does_nothing_at_all()
    {
        var scene = Build.SceneWith(Build.Item());

        var after = SceneReducer.Apply(
            scene,
            new TransformItem(new ItemId(Guid.NewGuid()), 0.1, 0.1, 0.1, 0, 1, 1),
            Screen);

        Assert.Equal(scene, after);
    }

    [Fact]
    public void Locking_touches_neither_the_position_nor_the_revision()
    {
        var item = Build.Item(centerX: 0.3, revision: 5);

        var after = SceneReducer.Apply(Build.SceneWith(item), new SetLocked(item.ItemId, true), Screen);

        Assert.Equal(item with { Locked = true }, after.Items[0]);
    }

    /// <summary>
    /// Unlocking all is one <see cref="SetLocked"/> per locked item in one patch, and nothing else
    /// moves - which is exactly why it needs no undo entry (Part 3, Part 4).
    /// </summary>
    [Fact]
    public void Unlocking_all_of_them_leaves_everything_where_it_was()
    {
        var items = Enumerable.Range(0, 5)
            .Select(index => Build.Item(centerX: 0.1 * index, rotationDeg: 30 * index, locked: index < 3))
            .ToArray();

        var scene = Build.SceneWith([.. items]);

        var after = items
            .Where(item => item.Locked)
            .Aggregate(scene, (current, item) => SceneReducer.Apply(current, new SetLocked(item.ItemId, false), Screen));

        Assert.All(after.Items, item => Assert.False(item.Locked));
        Assert.Equal(
            [.. items.Select(item => (item.CenterX, item.CenterY, item.Scale, item.RotationDeg, item.ZOrder))],
            [.. after.Items.Select(item => (item.CenterX, item.CenterY, item.Scale, item.RotationDeg, item.ZOrder))]);
    }

    /// <summary>
    /// <b>A picture in the fan is at the size it arrived at and stands straight</b> (Part 6, rebuilt
    /// at the end of M3). Nothing is remembered: dragging it back out has to be ONE movement, and a
    /// picture that changed size and angle in the moment it is grasped cannot be dragged.
    /// </summary>
    [Fact]
    public void Parking_puts_the_picture_into_the_fan_at_arrival_size_and_straight()
    {
        var item = Build.Item(centerX: 0.5, centerY: 0.5, scale: 0.3, rotationDeg: 45);

        var after = SceneReducer.Apply(
            Build.SceneWith(item),
            new ParkItem(item.ItemId, Parked: true, ZOrder: 4, Revision: 9, ParkedAt: 9),
            Screen);

        var parked = after.Items[0];

        Assert.True(parked.Parked);
        Assert.Equal(Layout.ScaleOnLoad(item.AspectRatio, Screen), parked.Scale);
        Assert.Equal(Screen.DefaultRotationDeg, parked.RotationDeg);
        Assert.Equal(9, parked.ParkedAt);
        Assert.Equal(Parking.Arrange(Build.SceneWith(parked), Screen).Items[0], parked);
    }

    /// <summary>
    /// Straight means the screen's OWN straight, not zero. A screen whose pictures arrive at 180°
    /// is one people sit at from the other side, and parking it to zero would put the bar upside
    /// down relative to everything else on that table.
    /// </summary>
    [Fact]
    public void Straight_is_the_angle_this_screen_hands_pictures_out_at()
    {
        var screen = Screen with { DefaultRotationDeg = 180 };
        var item = Build.Item(rotationDeg: 45);

        var after = SceneReducer.Apply(
            Build.SceneWith(item),
            new ParkItem(item.ItemId, Parked: true, ZOrder: 4, Revision: 9),
            screen);

        Assert.Equal(180, after.Items[0].RotationDeg);
    }

    /// <summary>
    /// Coming back out changes neither size nor angle: what the fan gave the picture is what comes
    /// onto the table. The price is named in Part 6 - whoever lined a picture up and then tidied it
    /// away lines it up again - and it buys the one thing that matters more, that pulling a picture
    /// out is a single continuous movement.
    /// </summary>
    [Fact]
    public void Unparking_changes_neither_size_nor_angle()
    {
        var item = Build.Item(rotationDeg: 45, scale: 0.8);
        var scene = Build.SceneWith(item);

        var parked = SceneReducer.Apply(
            scene, new ParkItem(item.ItemId, Parked: true, ZOrder: 4, Revision: 9, ParkedAt: 9), Screen);

        var back = SceneReducer.Apply(
            parked, new ParkItem(item.ItemId, Parked: false, ZOrder: 5, Revision: 10), Screen);

        Assert.Equal(parked.Items[0].RotationDeg, back.Items[0].RotationDeg);
        Assert.Equal(parked.Items[0].Scale, back.Items[0].Scale);
        Assert.Equal(0, back.Items[0].ParkedAt);
    }

    /// <summary>
    /// Taking one out of the bar closes it up again - and that happens through the reducer, so the
    /// table and the hub arrive at the same bar without a patch saying so.
    /// </summary>
    [Fact]
    public void Removing_a_parked_item_closes_the_bar_behind_it()
    {
        var items = Enumerable.Range(0, 3).Select(_ => Build.Item(parked: true)).ToArray();
        var scene = Parking.Arrange(Build.SceneWith([.. items]), Screen);

        var after = SceneReducer.Apply(scene, new RemoveItem(items[0].ItemId), Screen);

        Assert.Equal(2, after.Items.Count);
        Assert.NotEqual(scene.Items[1].CenterY, after.Items[0].CenterY);
        Assert.Equal(Parking.Arrange(after, Screen), after);
    }

    /// <summary>
    /// Unparking is the flag and the new <c>ZOrder</c>; where the picture goes is the gesture's
    /// business, and the bar behind it closes up (Part 3, Part 6).
    /// </summary>
    [Fact]
    public void Unparking_brings_the_item_to_the_front()
    {
        var item = Build.Item(parked: true, zOrder: 1);
        var scene = Parking.Arrange(Build.SceneWith(item, Build.Item(parked: true)), Screen);

        var after = SceneReducer.Apply(
            scene,
            new ParkItem(item.ItemId, Parked: false, ZOrder: 8, Revision: 3),
            Screen);

        Assert.False(after.Items[0].Parked);
        Assert.Equal(8, after.Items[0].ZOrder);
    }

    /// <summary>
    /// A screen whose park edge was changed during play: the whole bar follows at the next
    /// operation, because the positions are a function of the list rather than a stored place.
    /// </summary>
    [Fact]
    public void A_changed_park_edge_takes_effect_with_the_next_operation()
    {
        var item = Build.Item();
        var scene = SceneReducer.Apply(
            Build.SceneWith(item),
            new ParkItem(item.ItemId, Parked: true, ZOrder: 2, Revision: 2),
            Screen);

        var moved = SceneReducer.Apply(
            scene,
            new ParkItem(item.ItemId, Parked: true, ZOrder: 2, Revision: 3),
            Screen with { ParkEdge = ParkEdge.Left });

        Assert.True(scene.Items[0].CenterX > 0.5);
        Assert.True(moved.Items[0].CenterX < 0.5);
    }

    /// <summary>Determinism, for the three operations that move things (Part 11).</summary>
    [Fact]
    public void The_same_gesture_twice_gives_the_same_scene()
    {
        var item = Build.Item();
        var scene = Build.SceneWith(item, Build.Item(parked: true));

        var ops = new PatchOp[]
        {
            new TransformItem(item.ItemId, 0.7, 0.2, 0.4, 33, 5, 11),
            new SetLocked(item.ItemId, true),
            new ParkItem(item.ItemId, Parked: true, ZOrder: 6, Revision: 12),
        };

        static SceneState Run(SceneState scene, PatchOp[] ops, ScreenContext screen) =>
            ops.Aggregate(scene, (current, op) => SceneReducer.Apply(current, op, screen));

        Assert.Equal(Run(scene, ops, Screen), Run(scene, ops, Screen));
    }
}
