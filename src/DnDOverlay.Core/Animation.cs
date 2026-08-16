namespace DnDOverlay.Core;

/// <summary>What may move on one screen right now.</summary>
/// <param name="Background">Whether the background layer runs.</param>
/// <param name="Items">
/// The items that run, most important first. Everything not in here stands on its first frame - a
/// still picture rather than a missing one.
/// </param>
public sealed record AnimationPlan(bool Background, IReadOnlyList<ItemId> Items)
{
    /// <summary>Nothing moves - a blacked-out screen, or one with nothing animated on it.</summary>
    public static AnimationPlan Still { get; } = new(false, []);

    /// <summary>How many animations this plan runs, background included.</summary>
    public int Count => Items.Count + (Background ? 1 : 0);

    public bool Equals(AnimationPlan? other) =>
        other is not null && Background == other.Background && Items.SequenceEqual(other.Items);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Background);

        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Decides <b>which</b> pictures of a scene are allowed to move. How they move is the platform's
/// business and lives in the rendering project - the same split the decoder has, and for the same
/// reason: this part is a decision over the scene and is therefore testable without a window.
/// <para>
/// A continuous animation on a software-rendered transparent overlay is the most expensive case
/// this application has, which is why there is a ceiling at all (Part 6).
/// </para>
/// </summary>
public static class AnimationBudget
{
    /// <summary>
    /// Part 6's number for one screen. A starting point rather than a finding (Guide <c>G6</c>) -
    /// what it is worth shows at the table.
    /// </summary>
    public const int DefaultMaximum = 8;

    /// <summary>
    /// What may run, given a scene.
    /// <para>
    /// <b>The background goes first</b> when it moves: it fills the screen, so it is both the most
    /// expensive animation and the one whose loss is most visible. Items follow by
    /// <c>ZOrder</c>, topmost first - what lies on top is what is being looked at, and if something
    /// has to stand still it should be what is half covered anyway.
    /// </para>
    /// <para>
    /// A hidden layer contributes nothing: what is not drawn must not cost a timer either (Part 7,
    /// step 24). Same for a paused picture, which is the switch the DM has for exactly this.
    /// </para>
    /// </summary>
    public static AnimationPlan Plan(SceneState scene, int maximum = DefaultMaximum)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (maximum <= 0)
        {
            return AnimationPlan.Still;
        }

        var background =
            scene.BackgroundVisible
            && scene.Background is { AnimationPaused: false, Meta.IsAnimated: true };

        var left = maximum - (background ? 1 : 0);

        var items = scene.ItemsVisible
            ? scene.Items
                .OfType<ImageItem>()
                .Where(item => item is { AnimationPaused: false, Meta.IsAnimated: true })
                .OrderByDescending(item => item.ZOrder)
                .Take(Math.Max(0, left))
                .Select(item => item.ItemId)
                .ToList()
            : [];

        return new AnimationPlan(background, items);
    }
}
