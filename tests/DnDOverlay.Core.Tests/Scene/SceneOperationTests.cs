using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The eight operations M2b adds to the reducer. What they have in common is the shape of the
/// promise: they do the one thing they name, they leave everything else alone, and an operation
/// addressed at something this screen does not carry does <b>nothing</b> rather than failing
/// (Part 11).
/// </summary>
public sealed class SceneOperationTests
{
    private static readonly ScreenContext Screen = Build.Screen();

    [Fact]
    public void RemoveItem_takes_that_one_off_and_leaves_the_rest()
    {
        var goes = Build.Item();
        var stays = Build.Item();
        var scene = Build.SceneWith(goes, stays);

        var result = Apply(scene, new RemoveItem(goes.ItemId));

        Assert.Equal([stays], result.Items);
    }

    /// <summary>
    /// The open item from M1a, and it could not be shown until now: no operation addressed an
    /// EXISTING item, so "a patch for an unknown ItemId does nothing" had nothing to be unknown
    /// against.
    /// </summary>
    [Fact]
    public void A_patch_for_an_unknown_item_does_nothing_rather_than_failing()
    {
        var scene = Build.SceneWith(Build.Item());

        Assert.Equal(scene, Apply(scene, new RemoveItem(new ItemId(Guid.NewGuid()))));
        Assert.Equal(scene, Apply(scene, new SetShowName(new ItemId(Guid.NewGuid()), Show: true)));
        Assert.Equal(scene, Apply(scene, new SetAnimationPaused(new ItemId(Guid.NewGuid()), Paused: true)));
    }

    [Fact]
    public void SetBackground_replaces_whatever_was_there_and_leaves_the_items()
    {
        var item = Build.Item();
        var scene = Build.SceneWith(item) with { Background = Build.Background() };
        var wanted = Build.Background(asset: Build.Asset('d'), name: "Hafenviertel");

        var result = Apply(scene, new SetBackground(wanted));

        Assert.Equal(wanted, result.Background);
        Assert.Equal([item], result.Items);
    }

    /// <summary>
    /// Strictly separate from <c>ClearItems</c>, which is why "empty the lot" has to send both
    /// (Part 3). Here that separation is the assertion: the items are still standing.
    /// </summary>
    [Fact]
    public void ClearBackground_removes_the_background_and_only_that()
    {
        var item = Build.Item();
        var scene = Build.SceneWith(item) with { Background = Build.Background() };

        var result = Apply(scene, new ClearBackground());

        Assert.Null(result.Background);
        Assert.Equal([item], result.Items);
    }

    /// <summary>
    /// <c>SetName</c> addresses the ASSET: one picture, one name (Part 3). Two items showing the
    /// same picture are both renamed, and a third showing a different one is not - the second half
    /// is what makes the first half mean anything.
    /// </summary>
    [Fact]
    public void SetName_reaches_every_item_of_that_asset_and_no_other()
    {
        var asset = Build.Asset('e');
        var scene = Build.SceneWith(
            Build.Item(asset: asset, name: "alt"),
            Build.Item(asset: asset, name: "alt"),
            Build.Item(asset: Build.Asset('f'), name: "fremd"));

        var result = Apply(scene, new SetName(asset, "Ratsherr Vellin"));

        var names = result.Items.OfType<ImageItem>().Select(item => item.Name).ToList();
        Assert.Equal(["Ratsherr Vellin", "Ratsherr Vellin", "fremd"], names);
    }

    /// <summary>
    /// The background is the same picture when it carries the same asset, so it is renamed too -
    /// otherwise "one picture, one name" would hold for the items and quietly not for the layer
    /// underneath them.
    /// </summary>
    [Fact]
    public void SetName_reaches_the_background_when_it_shows_the_same_picture()
    {
        var asset = Build.Asset('e');
        var scene = Build.SceneWith(Build.Item(asset: asset)) with
        {
            Background = Build.Background(asset: asset, name: "alt"),
        };

        var result = Apply(scene, new SetName(asset, "Sturmküste"));

        Assert.Equal("Sturmküste", result.Background!.Name);
    }

    [Fact]
    public void SetName_for_a_picture_this_screen_does_not_show_changes_nothing()
    {
        var scene = Build.SceneWith(Build.Item()) with { Background = Build.Background() };

        Assert.Equal(scene, Apply(scene, new SetName(Build.Asset('z'), "Niemand")));
    }

    /// <summary>
    /// The counterpart to <see cref="SetName_reaches_every_item_of_that_asset_and_no_other"/>:
    /// the caption belongs to the INSTANCE. Both items show the same picture, and exactly one of
    /// them ends up wearing its name.
    /// </summary>
    [Fact]
    public void SetShowName_reaches_exactly_one_item_even_when_both_show_the_same_picture()
    {
        var asset = Build.Asset('e');
        var wanted = Build.Item(asset: asset);
        var other = Build.Item(asset: asset);
        var scene = Build.SceneWith(wanted, other);

        var result = Apply(scene, new SetShowName(wanted.ItemId, Show: true));

        Assert.True(result.Items.OfType<ImageItem>().Single(i => i.ItemId == wanted.ItemId).ShowName);
        Assert.False(result.Items.OfType<ImageItem>().Single(i => i.ItemId == other.ItemId).ShowName);
    }

    /// <summary>No item named means the background layer - a city map wants its name (Part 7).</summary>
    [Fact]
    public void SetShowName_without_an_item_means_the_background()
    {
        var scene = Build.SceneWith(Build.Item()) with { Background = Build.Background(showName: false) };

        var result = Apply(scene, new SetShowName(Item: null, Show: true));

        Assert.True(result.Background!.ShowName);
        Assert.False(result.Items.OfType<ImageItem>().Single().ShowName);
    }

    [Fact]
    public void SetShowName_without_an_item_and_without_a_background_changes_nothing()
    {
        var scene = Build.SceneWith(Build.Item());

        Assert.Equal(scene, Apply(scene, new SetShowName(Item: null, Show: true)));
    }

    [Fact]
    public void SetAnimationPaused_holds_one_item_still()
    {
        var item = Build.Item();
        var scene = Build.SceneWith(item);

        var result = Apply(scene, new SetAnimationPaused(item.ItemId, Paused: true));

        Assert.True(result.Items.OfType<ImageItem>().Single().AnimationPaused);
    }

    [Fact]
    public void SetAnimationPaused_without_an_item_holds_the_background_still()
    {
        var scene = SceneState.Empty with { Background = Build.Background() };

        var result = Apply(scene, new SetAnimationPaused(Item: null, Paused: true));

        Assert.True(result.Background!.AnimationPaused);
    }

    /// <summary>
    /// The two layers are independent in all four combinations - the promise step 24 is written
    /// against (Part 11). Checked as a table rather than as two switches, because the failure
    /// worth catching is one layer moving the other.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void The_two_layers_switch_independently(bool items, bool background)
    {
        var scene = Build.SceneWith(Build.Item()) with { Background = Build.Background() };

        var result = Apply(
            Apply(scene, new ToggleItems(items)),
            new ToggleBackground(background));

        Assert.Equal(items, result.ItemsVisible);
        Assert.Equal(background, result.BackgroundVisible);
    }

    /// <summary>
    /// Switching hides, it does not delete. The picture stays in the scene, which is what makes
    /// fading it back in immediate and free of a second transfer (Part 7, step 24).
    /// </summary>
    [Fact]
    public void Switching_a_layer_off_keeps_what_is_on_it()
    {
        var item = Build.Item();
        var background = Build.Background();
        var scene = Build.SceneWith(item) with { Background = background };

        var result = Apply(Apply(scene, new ToggleItems(false)), new ToggleBackground(false));

        Assert.Equal([item], result.Items);
        Assert.Equal(background, result.Background);
    }

    /// <summary>
    /// The switches carry the RESULT rather than flipping, so a patch that arrives twice after a
    /// reconnect lands on the same state - the same property <c>AddItem</c> has. A flip would
    /// pass every test above and be wrong exactly here.
    /// </summary>
    [Fact]
    public void An_operation_applied_twice_lands_where_it_landed_once()
    {
        var scene = Build.SceneWith(Build.Item()) with { Background = Build.Background() };

        foreach (var op in Operations(scene))
        {
            var once = Apply(scene, op);
            var twice = Apply(once, op);

            Assert.Equal(once, twice);
        }
    }

    /// <summary>
    /// None of these operations touches the revision, and that is a decision: <c>Revision</c>
    /// orders TRANSFORMS, and it is what a display weighs a running gesture against (Part 4).
    /// A rename that bumped it would look like a movement to the reconciliation built in M3.
    /// </summary>
    [Fact]
    public void None_of_these_operations_hands_out_a_revision()
    {
        var item = Build.Item(revision: 7);
        var scene = Build.SceneWith(item) with { Background = Build.Background() };

        foreach (var op in Operations(scene))
        {
            var revisions = Apply(scene, op).Items.Select(i => i.Revision).ToList();

            Assert.All(revisions, revision => Assert.Equal(7, revision));
        }
    }

    /// <summary>Every operation of this milestone, aimed at what the given scene actually holds.</summary>
    private static IEnumerable<PatchOp> Operations(SceneState scene)
    {
        var item = scene.Items[0];

        yield return new RemoveItem(item.ItemId);
        yield return new SetBackground(Build.Background(asset: Build.Asset('d')));
        yield return new ClearBackground();
        yield return new SetName(((ImageItem)item).AssetId, "Ratsherr Vellin");
        yield return new SetShowName(item.ItemId, Show: true);
        yield return new SetShowName(Item: null, Show: true);
        yield return new SetAnimationPaused(item.ItemId, Paused: true);
        yield return new SetAnimationPaused(Item: null, Paused: true);
        yield return new ToggleItems(false);
        yield return new ToggleBackground(false);
    }

    private static SceneState Apply(SceneState scene, PatchOp op) =>
        SceneReducer.Apply(scene, op, Screen);
}
