using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// A saved scene onto a screen (Part 3): new ids, depths in order, the target's fan and capping, a
/// fitted background fitted again - and replace versus add told apart by the operations alone.
/// </summary>
public sealed class SceneLoadingTests
{
    private static readonly ScreenContext Wide = Build.Screen();
    private static readonly ScreenContext Square = Build.Screen(width: 1200, height: 1200);

    private static long _revision = 100;

    [Fact]
    public void Loaded_items_get_new_ids_so_the_same_scene_can_lie_twice()
    {
        var saved = Build.SceneWith(Build.Item(), Build.Item());

        var first = Added(SceneLoading.Ops(saved, SceneState.Empty, Wide, SceneLoad.Add, Next));
        var second = Added(SceneLoading.Ops(saved, SceneState.Empty, Wide, SceneLoad.Add, Next));

        Assert.Empty(first.Select(item => item.ItemId).Intersect(saved.Items.Select(item => item.ItemId)));
        Assert.Empty(first.Select(item => item.ItemId).Intersect(second.Select(item => item.ItemId)));
    }

    /// <summary>Replacing clears first - and on an empty screen there is nothing to clear, so nothing is said.</summary>
    [Fact]
    public void Replacing_clears_what_lies_there_and_adding_does_not()
    {
        var saved = Build.SceneWith(Build.Item());
        var target = Build.SceneWith(Build.Item(zOrder: 7)) with { Background = Build.Background() };

        var replacing = SceneLoading.Ops(saved, target, Wide, SceneLoad.Replace, Next);
        var adding = SceneLoading.Ops(saved, target, Wide, SceneLoad.Add, Next);

        Assert.IsType<ClearItems>(replacing[0]);
        Assert.IsType<ClearBackground>(replacing[1]);
        Assert.All(adding, op => Assert.IsType<AddItem>(op));
        Assert.Empty(SceneLoading.Ops(saved, SceneState.Empty, Wide, SceneLoad.Replace, Next).OfType<ClearItems>());
    }

    /// <summary>From nothing when replacing, above what lies there when adding - and the saved order kept.</summary>
    [Fact]
    public void Depths_keep_their_order_from_zero_or_above_the_top()
    {
        var low = Build.Item(zOrder: 4, name: "unten");
        var high = Build.Item(zOrder: 9, name: "oben");
        var saved = Build.SceneWith(high, low);
        var target = Build.SceneWith(Build.Item(zOrder: 20));

        var replaced = Added(SceneLoading.Ops(saved, target, Wide, SceneLoad.Replace, Next));
        var added = Added(SceneLoading.Ops(saved, target, Wide, SceneLoad.Add, Next));

        Assert.Equal([("unten", 0), ("oben", 1)], replaced.Select(item => (((ImageItem)item).Name, item.ZOrder)));
        Assert.Equal([("unten", 21), ("oben", 22)], added.Select(item => (((ImageItem)item).Name, item.ZOrder)));
    }

    /// <summary>A parked picture goes into the target's fan, and the fan counts its own depths.</summary>
    [Fact]
    public void Parked_pictures_count_in_the_fan_of_the_target()
    {
        var saved = Build.SceneWith(Build.Item(parked: true, zOrder: 3), Build.Item(zOrder: 1));
        var target = Build.SceneWith(Build.Item(parked: true, zOrder: 5), Build.Item(zOrder: 2));

        var added = Added(SceneLoading.Ops(saved, target, Wide, SceneLoad.Add, Next));

        Assert.Equal(6, Assert.Single(added, item => item.Parked).ZOrder);
        Assert.Equal(3, Assert.Single(added, item => !item.Parked).ZOrder);
    }

    /// <summary>A panorama from a wide table is capped on a square one, as a picture moved there would be.</summary>
    [Fact]
    public void Pictures_are_capped_against_the_target()
    {
        var panorama = Build.Item(scale: 0.5, aspectRatio: 3.5);
        var saved = Build.SceneWith(panorama);

        var loaded = Assert.Single(Added(SceneLoading.Ops(saved, SceneState.Empty, Square, SceneLoad.Add, Next)));

        Assert.True(loaded.Scale < panorama.Scale);
        Assert.True(loaded.Scale <= Layout.WidthCap(panorama.AspectRatio, Square) + 1e-9);
    }

    /// <summary>Siting question 5: a filled background is filled again on the target; one placed by hand keeps its place.</summary>
    [Fact]
    public void A_fitted_background_is_fitted_again_and_one_placed_by_hand_keeps_its_place()
    {
        var filled = Build.Background() with { Fit = BackgroundFit.Cover };
        var placed = Build.Background() with { Fit = null, CenterX = 0.5, CenterY = 0.5, Scale = 1.2 };

        var refitted = Background(SceneLoading.Ops(SceneState.Empty with { Background = filled }, SceneState.Empty, Square, SceneLoad.Add, Next));
        var kept = Background(SceneLoading.Ops(SceneState.Empty with { Background = placed }, SceneState.Empty, Square, SceneLoad.Add, Next));

        var (_, scale) = Layout.FitBackground(filled.Meta.AspectRatio, BackgroundFit.Cover, Square, 0);

        Assert.Equal(scale, refitted.Scale, precision: 9);
        Assert.Equal(BackgroundFit.Cover, refitted.Fit);
        Assert.Null(kept.Fit);
        Assert.Equal(0.5, kept.CenterX, precision: 9);
    }

    [Fact]
    public void Nothing_about_a_focus_travels()
    {
        var item = Build.Item();
        var saved = Build.SceneWith(item) with { FocusItems = [item.ItemId] };

        var ops = SceneLoading.Ops(saved, SceneState.Empty, Wide, SceneLoad.Replace, Next);

        Assert.DoesNotContain(ops, op => op is SetFocus);
    }

    private static long Next() => Interlocked.Increment(ref _revision);

    private static List<SceneItem> Added(IReadOnlyList<PatchOp> ops) => [.. ops.OfType<AddItem>().Select(add => add.Item)];

    private static BackgroundItem Background(IReadOnlyList<PatchOp> ops) => Assert.Single(ops.OfType<SetBackground>()).Background;
}
