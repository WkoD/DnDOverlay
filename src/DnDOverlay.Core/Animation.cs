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

/// <summary>What a place on the screen is showing at the moment.</summary>
public enum PictureState
{
    /// <summary>Nothing yet - the place has just been made.</summary>
    Nothing,

    /// <summary>A picture that does not move, and has no animation attached to it.</summary>
    Still,

    /// <summary>An animation, running.</summary>
    Moving,

    /// <summary>An animation, stopped where it stood and able to carry on.</summary>
    Held,
}

/// <summary>What to do with a place on the screen to bring it up to date.</summary>
public enum PictureAction
{
    /// <summary>Nothing. It already shows what it should, in the state it should.</summary>
    Leave,

    /// <summary>Build the animation and run it, from the beginning.</summary>
    Start,

    /// <summary>Start the animation that is already there again, where it stopped.</summary>
    Resume,

    /// <summary>Stop the animation where it stands, keeping it.</summary>
    Hold,

    /// <summary>Show the still picture and let the animation go.</summary>
    Freeze,
}

/// <summary>
/// What has to happen to one place on the screen when the scene changes. A pure function over
/// before and after, so it is decided here rather than inside a window.
/// <para>
/// <b>Why this is not simply "draw it again":</b> measured at the table (hand-run of M2b, step 24),
/// every change to the scene - switching the background on, renaming something - restarted every
/// animation from its first frame, because the display rebuilt the whole picture on every patch. An
/// animation is not a drawing, it is a running clock, and rebuilding it is not the same as leaving
/// it alone.
/// </para>
/// </summary>
public static class PictureTransition
{
    /// <summary>
    /// What to do with a place that currently shows <paramref name="showing"/> in state
    /// <paramref name="state"/>, when it should show <paramref name="wanted"/>.
    /// </summary>
    /// <param name="sameRendering">
    /// Whether what is mounted is still the same <b>rendering</b> of that asset.
    /// <para>
    /// The identifier alone is <b>not</b> the identity, and that is measured rather than foreseen
    /// (hand-run of M2b, second round, step 17): one asset arrives <b>twice</b> - the thumbnail
    /// first, so the picture stands at its place blurred within a second, and the original after it
    /// (Part 5). Both carry the same <see cref="AssetId"/>. Comparing identifiers alone therefore
    /// answers "nothing changed" to the very arrival the whole two-step exists for, and the table
    /// keeps the blurred one for good.
    /// </para>
    /// </param>
    /// <param name="admitted">Whether <see cref="AnimationBudget"/> lets this one move.</param>
    /// <param name="paused">
    /// Whether the DM stopped it. This is what tells a pause apart from a refusal: both end with a
    /// picture that does not move, but a pause is meant to be undone and therefore keeps its place
    /// in the animation, while a refusal has to let go of what it holds.
    /// </param>
    public static PictureAction Next(
        PictureState state,
        AssetId? showing,
        AssetId wanted,
        bool sameRendering,
        bool admitted,
        bool paused)
    {
        if (showing != wanted || !sameRendering)
        {
            // A different picture. Nothing of the old one can be carried over.
            return admitted ? PictureAction.Start : PictureAction.Freeze;
        }

        if (admitted)
        {
            return state switch
            {
                PictureState.Moving => PictureAction.Leave,
                PictureState.Held => PictureAction.Resume,
                _ => PictureAction.Start,
            };
        }

        if (paused)
        {
            // Standing still already, in one way or the other. A still picture cannot be held - it
            // has no animation left to hold - and re-freezing it would only make it flicker.
            return state == PictureState.Moving ? PictureAction.Hold : PictureAction.Leave;
        }

        // The budget turned it away, or it never moved at all.
        return state == PictureState.Still ? PictureAction.Leave : PictureAction.Freeze;
    }

    /// <summary>What the place shows once <paramref name="action"/> has been carried out.</summary>
    /// <summary>
    /// Whether this action actually builds a picture into the tree - the expensive half.
    /// <para>
    /// It is what the one-per-render-pass budget counts (Part 11, the priority rule): twenty
    /// pictures finishing at once would otherwise all be hung up in the drawing that a hand at the
    /// table is waiting for. Starting an animation and putting a still up cost; resuming one that
    /// is already there, holding it, or leaving a place alone do not.
    /// </para>
    /// </summary>
    public static bool Costs(PictureAction action) =>
        action is PictureAction.Start or PictureAction.Freeze;

    public static PictureState After(PictureState state, PictureAction action) => action switch
    {
        PictureAction.Start or PictureAction.Resume => PictureState.Moving,
        PictureAction.Hold => PictureState.Held,
        PictureAction.Freeze => PictureState.Still,
        _ => state,
    };
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
    /// How many items a screen is reckoned to carry at 1080p - derived from the budget rather than
    /// set (Part 6). It is not a limit anybody enforces; it is the number past which the display is
    /// beyond what it was measured for, and the point at which per-item feedback stops being worth
    /// its cost (measured in the hand-run of M3b: 722 loading pictures, and the rings alone held the
    /// UI thread for seconds).
    /// </summary>
    public const int ItemsPerScreen = 30;

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
