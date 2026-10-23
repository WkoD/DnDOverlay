using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// A gesture of this device's own hands lands in its own scene at once, raised by the hub's arithmetic
/// and numbered by nobody but the hub (Part 3, Part 4 conflict rules 1 and 2).
/// </summary>
public sealed class LocalGestureTests
{
    private static readonly ItemId Dragon = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    private static readonly ItemId Goblin = new(Guid.Parse("10000000-0000-0000-0000-000000000002"));
    private static readonly ItemId Card = new(Guid.Parse("10000000-0000-0000-0000-000000000003"));
    private static readonly ScreenContext Screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);

    /// <summary>Letting go puts the picture where the hand left it, at its depth and its revision.</summary>
    [Fact]
    public void A_release_moves_the_picture_and_keeps_depth_and_revision()
    {
        var scene = Scene(Item(Dragon, zOrder: 1, revision: 8), Item(Goblin, zOrder: 5));

        var after = LocalGesture.Apply(scene, new ItemTransform(Dragon, 0.4, 0.6, 0.2, 0), toFront: false, Screen);

        var moved = Find(after, Dragon);
        Assert.Equal(0.4, moved.CenterX, 6);
        Assert.Equal(0.6, moved.CenterY, 6);
        Assert.Equal(1, moved.ZOrder);
        Assert.Equal(8, moved.Revision);
    }

    /// <summary>The grab brings it to the front of the table at once - one above the top.</summary>
    [Fact]
    public void A_grab_raises_the_picture_above_the_table()
    {
        var scene = Scene(Item(Dragon, zOrder: 1, revision: 8), Item(Goblin, zOrder: 5));

        var after = LocalGesture.Apply(scene, Where(Dragon), toFront: true, Screen);

        Assert.Equal(6, Find(after, Dragon).ZOrder);
        Assert.Equal(8, Find(after, Dragon).Revision);
    }

    /// <summary>The hub's formula to the letter, so the answer confirms rather than corrects: the top itself goes one higher.</summary>
    [Fact]
    public void A_grab_on_the_top_picture_raises_it_by_one_like_the_hub()
    {
        var scene = Scene(Item(Dragon, zOrder: 9), Item(Goblin, zOrder: 5));

        var after = LocalGesture.Apply(scene, Where(Dragon), toFront: true, Screen);

        Assert.Equal(10, Find(after, Dragon).ZOrder);
    }

    /// <summary>A card is taken hold of in the fan too: it rises among the cards, not into the table's number space.</summary>
    [Fact]
    public void A_grab_in_the_fan_raises_within_the_fan()
    {
        var scene = Scene(Item(Dragon, zOrder: 40), Item(Goblin, zOrder: 3, parked: true), Item(Card, zOrder: 1, parked: true));

        var after = LocalGesture.Apply(scene, Where(Card), toFront: true, Screen);

        Assert.Equal(4, Find(after, Card).ZOrder);
    }

    /// <summary>A gesture for a picture that has gone, or a screen without a scene, writes nothing - no empty scene is invented.</summary>
    [Fact]
    public void Nothing_to_lay_it_into_leaves_the_book_alone()
    {
        Assert.Null(LocalGesture.Apply(null, Where(Dragon), toFront: true, Screen));
        Assert.Null(LocalGesture.Apply(Scene(Item(Goblin, zOrder: 1)), Where(Dragon), toFront: false, Screen));
    }

    private static ItemTransform Where(ItemId item) => new(item, 0.5, 0.5, 0.2, 0);

    private static SceneItem Find(SceneState? scene, ItemId item) =>
        Assert.Single(Assert.IsType<SceneState>(scene).Items, one => one.ItemId == item);

    private static TextItem Item(ItemId id, int zOrder, long revision = 1, bool parked = false) =>
        new(id, 0.5, 0.5, 0.2, 1, 0, zOrder, false, parked, revision, "Dilwyn");

    private static SceneState Scene(params SceneItem[] items) => SceneState.Empty with { Items = [.. items] };
}
