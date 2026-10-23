using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>What lies on top of what at the table: the table, the fan, a hand, the focus (Part 3, Part 6).</summary>
public sealed class StageDepthTests
{
    private static readonly ItemId Id = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));

    [Fact]
    public void A_picture_on_the_table_lies_at_the_hub_s_depth()
    {
        Assert.Equal(7, StageDepth.Of(Item(zOrder: 7), focusIndex: null, inHand: false));
    }

    /// <summary>The way to get a picture back is never covered by the table it was tidied off.</summary>
    [Fact]
    public void The_fan_lies_above_the_whole_table()
    {
        var card = StageDepth.Of(Item(zOrder: 0, parked: true), focusIndex: null, inHand: false);
        var topOfTable = StageDepth.Of(Item(zOrder: Parking.FanAbove - 1), focusIndex: null, inHand: false);

        Assert.True(card > topOfTable);
    }

    /// <summary>
    /// A picture in a hand lies above everything the hub hands out - an arrival lands below it (hand-run
    /// of M3, step 17) - and two held pictures keep their order.
    /// </summary>
    [Fact]
    public void A_held_picture_lies_above_the_fan_and_keeps_its_order()
    {
        var held = StageDepth.Of(Item(zOrder: 3), focusIndex: null, inHand: true);
        var heldHigher = StageDepth.Of(Item(zOrder: 4), focusIndex: null, inHand: true);
        var topCard = StageDepth.Of(Item(zOrder: 100_000, parked: true), focusIndex: null, inHand: false);

        Assert.True(held > topCard);
        Assert.True(heldHigher > held);
    }

    /// <summary>The focus outranks everything, a held picture included, in the order of the focus.</summary>
    [Fact]
    public void The_focus_lies_above_everything_in_its_own_order()
    {
        var first = StageDepth.Of(Item(zOrder: 900), focusIndex: 0, inHand: true);
        var second = StageDepth.Of(Item(zOrder: 1), focusIndex: 1, inHand: false);
        var held = StageDepth.Of(Item(zOrder: 100_000), focusIndex: null, inHand: true);

        Assert.True(first > held);
        Assert.Equal(first + 1, second);
    }

    private static TextItem Item(int zOrder, bool parked = false) =>
        new(Id, 0.5, 0.5, 0.2, 1, 0, zOrder, false, parked, 1, "Dilwyn");
}
