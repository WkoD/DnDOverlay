using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

public sealed class SceneReducerTests
{
    [Fact]
    public void AddItem_puts_the_item_into_the_scene()
    {
        var item = Build.Item();

        var result = SceneReducer.Apply(SceneState.Empty, new AddItem(item), Build.Screen());

        Assert.Equal([item], result.Items);
    }

    /// <summary>
    /// Not a normal case - a patch delivered twice after a reconnect is. Replacing leaves the
    /// scene consistent, appending would show the same image twice with one identifier.
    /// </summary>
    [Fact]
    public void AddItem_with_a_known_id_replaces_instead_of_duplicating()
    {
        var id = new ItemId(Guid.NewGuid());
        var first = Build.Item(id: id, centerX: 0.2);
        var second = Build.Item(id: id, centerX: 0.8);

        var scene = SceneReducer.Apply(SceneState.Empty, new AddItem(first), Build.Screen());
        var result = SceneReducer.Apply(scene, new AddItem(second), Build.Screen());

        Assert.Equal([second], result.Items);
    }

    /// <summary>
    /// Rule 7 as a property rather than a promise: an older display facing a newer control is
    /// simply one that does not know a few operations, and it must not lose the rest of the
    /// scene over that.
    /// </summary>
    [Fact]
    public void An_unknown_operation_leaves_the_scene_untouched()
    {
        var scene = Build.SceneWith(Build.Item());

        var result = SceneReducer.Apply(scene, new UnknownToThisBuild(), Build.Screen());

        Assert.Equal(scene, result);
    }

    /// <summary>
    /// The scene is a value, so applying to it must not change what was handed in - the undo
    /// timeline keeps old states around and would otherwise watch them mutate under its hands.
    /// </summary>
    [Fact]
    public void Applying_does_not_change_the_scene_it_was_given()
    {
        var scene = Build.SceneWith(Build.Item());
        var before = scene.Items.ToList();

        _ = SceneReducer.Apply(scene, new AddItem(Build.Item()), Build.Screen());

        Assert.Equal(before, scene.Items);
    }

    /// <summary>Same patch, same starting point, twice - identical result, identifiers included.</summary>
    [Fact]
    public void The_same_patch_on_the_same_state_gives_the_same_result()
    {
        var op = new AddItem(Build.Item());

        var first = SceneReducer.Apply(SceneState.Empty, op, Build.Screen());
        var second = SceneReducer.Apply(SceneState.Empty, op, Build.Screen());

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Records compare list members by reference, and half a dozen promises in Part 11 are
    /// phrased as "twice the same result". This is the test that would have caught it.
    /// </summary>
    [Fact]
    public void Two_scenes_with_equal_items_in_separate_lists_are_equal()
    {
        var item = Build.Item();

        Assert.Equal(Build.SceneWith(item), Build.SceneWith(item));
        Assert.Equal(Build.SceneWith(item).GetHashCode(), Build.SceneWith(item).GetHashCode());
    }

    /// <summary>
    /// <b>Putting a picture away takes the padlock off it.</b> The lock holds a picture where it
    /// LIES, so that a hand at the table cannot push it; in the fan there is no such place - the fan
    /// owns every card's size, angle and position - and a padlock there refuses only the one gesture
    /// that matters, pulling the card back out. Read at the table in the second hand-run of M4.
    /// </summary>
    [Fact]
    public void Parking_a_locked_picture_takes_the_padlock_off()
    {
        var screen = Build.Screen();
        var locked = Build.Item() with { Locked = true };
        var scene = Build.SceneWith(locked);

        var parked = SceneReducer.Apply(
            scene,
            new ParkItem(locked.ItemId, Parked: true, ZOrder: locked.ZOrder, Revision: 2),
            screen);

        Assert.False(parked.Items.Single().Locked, "the padlock came into the fan with it");
    }

    /// <summary>
    /// The counter-check: it is PARKING that unlocks, not the reducer helping itself to the flag.
    /// A picture that is locked while it lies out stays locked, and one coming back out of the fan
    /// comes back unlocked rather than picking its old padlock up again (Guide <c>C16</c>).
    /// </summary>
    [Fact]
    public void A_picture_lying_out_keeps_its_padlock()
    {
        var screen = Build.Screen();
        var locked = Build.Item() with { Locked = true, Parked = true };
        var scene = Build.SceneWith(locked);

        var freed = SceneReducer.Apply(
            scene,
            new ParkItem(locked.ItemId, Parked: false, ZOrder: 9, Revision: 3),
            screen);

        Assert.True(freed.Items.Single().Locked, "coming out of the fan lost a lock it should keep");
    }

    private sealed record UnknownToThisBuild : PatchOp;
}
