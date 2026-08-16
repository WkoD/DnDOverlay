using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// Which pictures are allowed to move. A continuous animation on a software-rendered transparent
/// overlay is the most expensive case this application has, so there is a ceiling - and what falls
/// outside it stands on its first frame rather than disappearing (Part 6).
/// </summary>
public sealed class AnimationBudgetTests
{
    [Fact]
    public void A_scene_with_nothing_animated_runs_nothing()
    {
        var scene = Build.SceneWith(Build.Item(), Build.Item());

        Assert.Equal(AnimationPlan.Still, AnimationBudget.Plan(scene));
    }

    [Fact]
    public void An_animated_picture_runs()
    {
        var item = Moving();
        var scene = Build.SceneWith(item);

        var plan = AnimationBudget.Plan(scene);

        Assert.Equal([item.ItemId], plan.Items);
        Assert.False(plan.Background);
    }

    /// <summary>The switch the DM has for exactly this case (Part 6).</summary>
    [Fact]
    public void A_paused_picture_does_not_run()
    {
        var scene = Build.SceneWith(Moving() with { AnimationPaused = true });

        Assert.Equal(AnimationPlan.Still, AnimationBudget.Plan(scene));
    }

    /// <summary>
    /// What is not drawn must not cost a timer either. Hiding a layer is what makes fading it back
    /// in free - it would be a poor bargain if it kept paying for animations meanwhile (step 24).
    /// </summary>
    [Fact]
    public void A_hidden_layer_costs_nothing()
    {
        var scene = Build.SceneWith(Moving()) with
        {
            Background = MovingBackground(),
            ItemsVisible = false,
            BackgroundVisible = false,
        };

        Assert.Equal(AnimationPlan.Still, AnimationBudget.Plan(scene));
    }

    /// <summary>
    /// The background fills the screen: the most expensive animation, and the one whose loss is
    /// most visible. It gets its slot first and takes one from the items.
    /// </summary>
    [Fact]
    public void The_background_goes_first_and_takes_a_slot_from_the_items()
    {
        var scene = Build.SceneWith([.. Enumerable.Range(0, 8).Select(z => Moving(z))]) with
        {
            Background = MovingBackground(),
        };

        var plan = AnimationBudget.Plan(scene);

        Assert.True(plan.Background);
        Assert.Equal(8, plan.Count);
        Assert.Equal(7, plan.Items.Count);
    }

    /// <summary>
    /// Over the ceiling the topmost win. What lies on top is what is being looked at; if something
    /// has to stand still it should be what is half covered anyway.
    /// </summary>
    [Fact]
    public void Over_the_ceiling_the_topmost_pictures_win()
    {
        var items = Enumerable.Range(0, 12).Select(z => Moving(z)).ToArray();
        var scene = Build.SceneWith(items);

        var plan = AnimationBudget.Plan(scene, maximum: 3);

        Assert.Equal(
            [items[11].ItemId, items[10].ItemId, items[9].ItemId],
            plan.Items);
    }

    /// <summary>
    /// The ceiling is a number, not a feeling - so it is asserted as one. A plan that returned
    /// everything would pass every test above.
    /// </summary>
    [Fact]
    public void Never_more_than_the_ceiling()
    {
        var scene = Build.SceneWith([.. Enumerable.Range(0, 30).Select(z => Moving(z))]) with
        {
            Background = MovingBackground(),
        };

        Assert.Equal(AnimationBudget.DefaultMaximum, AnimationBudget.Plan(scene).Count);
    }

    /// <summary>
    /// Blackout stops the timers (Part 6). It is expressed as a ceiling of zero rather than as a
    /// case of its own - the caller says how much may run, and none is a number.
    /// </summary>
    [Fact]
    public void A_ceiling_of_nothing_runs_nothing()
    {
        var scene = Build.SceneWith(Moving()) with { Background = MovingBackground() };

        Assert.Equal(AnimationPlan.Still, AnimationBudget.Plan(scene, maximum: 0));
    }

    private static ImageItem Moving(int zOrder = 0) =>
        Build.Item(zOrder: zOrder) with { Meta = Build.Meta() with { IsAnimated = true } };

    private static BackgroundItem MovingBackground() =>
        Build.Background() with { Meta = Build.Meta() with { IsAnimated = true } };
}
