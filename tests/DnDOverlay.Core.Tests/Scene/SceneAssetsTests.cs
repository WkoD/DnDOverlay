using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// Which pictures a device is still standing on. Three stores ask it - decoded bitmaps, the bytes
/// kept for animations, and the picture store on disk - and if they disagree, one frees something
/// another still draws.
/// <para>
/// It exists because the display did not ask at all: measured at the table (hand-run of M2b,
/// step 44a) it went to 2 GB and kept climbing, because a table keyed by identifier only ever grew
/// - the convenience Part 6 rules out by name.
/// </para>
/// </summary>
public sealed class SceneAssetsTests
{
    [Fact]
    public void Nothing_is_needed_by_nothing()
    {
        Assert.Empty(SceneAssets.InUse([]));
        Assert.Empty(SceneAssets.InUse([SceneState.Empty]));
    }

    [Fact]
    public void Items_and_background_both_count()
    {
        var scene = Build.SceneWith(Build.Item(asset: Build.Asset('a'))) with
        {
            Background = Build.Background(asset: Build.Asset('b')),
        };

        Assert.Equal(
            [Build.Asset('a'), Build.Asset('b')],
            SceneAssets.InUse([scene]).OrderBy(asset => asset.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Two items on one picture are one picture. Without this the sweep would be right anyway, but
    /// the counted number is what a store trims against - and counting it twice invites the next
    /// reader to treat the set as a tally.
    /// </summary>
    [Fact]
    public void Two_items_on_one_picture_name_it_once()
    {
        var asset = Build.Asset('a');
        var scene = Build.SceneWith(Build.Item(asset: asset), Build.Item(asset: asset));

        Assert.Single(SceneAssets.InUse([scene]));
    }

    /// <summary>
    /// Counted over ALL screens, and that is the half that matters: a screen without an overlay
    /// keeps its arrangement (Part 3), so what lies there is still needed. Freeing it would empty
    /// a table the moment its screen was switched off.
    /// </summary>
    [Fact]
    public void A_screen_that_is_not_drawn_still_needs_its_pictures()
    {
        var drawn = Build.SceneWith(Build.Item(asset: Build.Asset('a')));
        var idle = Build.SceneWith(Build.Item(asset: Build.Asset('b'))) with
        {
            ItemsVisible = false,
            BackgroundVisible = false,
        };

        var wanted = SceneAssets.InUse([drawn, idle]);

        Assert.Contains(Build.Asset('b'), wanted);
        Assert.Equal(2, wanted.Count);
    }

    /// <summary>
    /// The assertion the whole thing exists for: what was taken off the screens is no longer
    /// needed. A set that only ever grew would pass every test above.
    /// </summary>
    [Fact]
    public void What_was_removed_is_no_longer_needed()
    {
        var stays = Build.Item(asset: Build.Asset('a'));
        var goes = Build.Item(asset: Build.Asset('b'));

        var before = Build.SceneWith(stays, goes);
        var after = SceneReducer.Apply(before, new RemoveItem(goes.ItemId), Build.Screen());

        Assert.Equal(2, SceneAssets.InUse([before]).Count);
        Assert.Equal([Build.Asset('a')], SceneAssets.InUse([after]));
    }

    /// <summary>
    /// Hiding a layer is not removing it - the pictures stay in the scene and stay needed, which is
    /// what makes fading them back in free of a second transfer (Part 7, step 24). Freeing them on
    /// a switch would turn the cheapest operation into the most expensive.
    /// </summary>
    [Fact]
    public void Switching_a_layer_off_does_not_free_its_pictures()
    {
        var scene = Build.SceneWith(Build.Item(asset: Build.Asset('a'))) with
        {
            Background = Build.Background(asset: Build.Asset('b')),
        };

        var hidden = SceneReducer.Apply(
            SceneReducer.Apply(scene, new ToggleItems(false), Build.Screen()),
            new ToggleBackground(false),
            Build.Screen());

        Assert.Equal(2, SceneAssets.InUse([hidden]).Count);
    }
}
