using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>The focus as it is drawn (Part 3): placed by count, fitted with its own angle, in the order chosen.</summary>
public sealed class FocusTests
{
    private static readonly ScreenContext Screen = Build.Screen();

    /// <summary>No focus, nothing to arrange - and the scene itself is never what changes.</summary>
    [Fact]
    public void Without_a_focus_nothing_is_arranged()
    {
        Assert.Empty(Focus.Arrange(Build.SceneWith(Build.Item()), Screen));
    }

    /// <summary>One picture stands in the middle, as large as the screen allows; its angle stays the DM's.</summary>
    [Fact]
    public void One_picture_stands_in_the_middle_and_fills_the_height()
    {
        var item = Build.Item(centerX: 0.2, centerY: 0.8, scale: 0.1, aspectRatio: 1, rotationDeg: 0);
        var drawn = Focus.Arrange(Build.SceneWith(item) with { FocusItems = [item.ItemId] }, Screen).Single();

        Assert.Equal(0.5, drawn.CenterX, 6);
        Assert.Equal(0.5, drawn.CenterY, 6);
        Assert.Equal(0.92, drawn.Scale, 6);
        Assert.Equal(item.RotationDeg, drawn.RotationDeg);
    }

    /// <summary>A wide picture turned on its side is fitted by its turned hull, not by its upright shape.</summary>
    [Fact]
    public void A_turned_picture_is_fitted_by_its_hull()
    {
        var upright = Build.Item(aspectRatio: 3);
        var lying = Build.Item(aspectRatio: 3, rotationDeg: 90);

        var uprightScale = Focus.Arrange(Build.SceneWith(upright) with { FocusItems = [upright.ItemId] }, Screen).Single().Scale;
        var lyingScale = Focus.Arrange(Build.SceneWith(lying) with { FocusItems = [lying.ItemId] }, Screen).Single().Scale;

        // Upright, 3:1 meets the width of a 16:9 screen; on its side, its length stands up the height.
        Assert.Equal(0.92 * (16d / 9) / 3, uprightScale, 6);
        Assert.Equal(0.92 / 3, lyingScale, 6);
    }

    /// <summary>Two stand side by side, in the order they were chosen - not by depth.</summary>
    [Fact]
    public void Two_stand_side_by_side_in_the_order_chosen()
    {
        var first = Build.Item(zOrder: 5);
        var second = Build.Item(zOrder: 1);
        var drawn = Focus.Arrange(Build.SceneWith(first, second) with { FocusItems = [second.ItemId, first.ItemId] }, Screen);

        Assert.Equal([second.ItemId, first.ItemId], drawn.Select(item => item.ItemId));
        Assert.True(drawn[0].CenterX < 0.5 && drawn[1].CenterX > 0.5);
        Assert.Equal(drawn[0].CenterY, drawn[1].CenterY, 6);
    }

    /// <summary>Five stand in two rows of three, the short second row centred.</summary>
    [Fact]
    public void A_short_last_row_stands_centred()
    {
        var items = Enumerable.Range(0, 5).Select(_ => Build.Item()).ToArray();
        var drawn = Focus.Arrange(Build.SceneWith(items) with { FocusItems = [.. items.Select(item => item.ItemId)] }, Screen);

        Assert.Equal(3, drawn.Count(item => item.CenterY < 0.5));
        Assert.Equal(0.5, (drawn[3].CenterX + drawn[4].CenterX) / 2, 6);
        Assert.Equal(0.5, drawn[1].CenterX, 6);
    }

    /// <summary>An id the scene no longer holds is left out; the rest keep their focus.</summary>
    [Fact]
    public void A_vanished_item_is_left_out()
    {
        var item = Build.Item();
        var drawn = Focus.Arrange(Build.SceneWith(item) with { FocusItems = [new ItemId(Guid.NewGuid()), item.ItemId] }, Screen);

        Assert.Equal(item.ItemId, drawn.Single().ItemId);
        Assert.Equal(0.5, drawn.Single().CenterX, 6);
    }
}
