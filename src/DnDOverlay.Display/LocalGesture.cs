using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// Lays a gesture of this device's own hands into its own scene at once, through the reducer both
/// ends share - conflict rule 2: "the display applies its gesture locally at once and corrects as soon
/// as a broadcast arrives that it did not cause itself" (Part 4).
/// <para>
/// <b>The revision stays as it was</b>: numbering is the hub's alone (conflict rule 1), and a display
/// that invented one would win a comparison it has no business winning. What this changes is only
/// WHERE the picture is drawn - and how high it lies - until the hub says so too.
/// </para>
/// <para>
/// <b>The grab raises it here too</b>, by the hub's own arithmetic, deliberately duplicated rather than
/// approximated: the top of the picture's OWN layer plus one - the table's or the fan's - unless it
/// already lies higher. Part 3 promises that what is taken hold of comes to the front "locally at once
/// and bindingly from the hub right afterwards", and the second half was the only half built: under
/// load the runner saw every pushed picture "touched again" at the end of the load, one after another,
/// as the queued answers finally arrived with the depth each grab had earned. A display that guessed a
/// different depth would be corrected visibly.
/// </para>
/// </summary>
internal static class LocalGesture
{
    /// <summary>
    /// The scene with the gesture laid in, or <see langword="null"/> when there is nothing to lay it
    /// into - no scene, or the item is gone - so the book is left as it is.
    /// </summary>
    /// <param name="toFront">Whether this is the grab.</param>
    internal static SceneState? Apply(SceneState? scene, ItemTransform transform, bool toFront, ScreenContext context)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(context);

        if (scene?.Items.FirstOrDefault(item => item.ItemId == transform.Item) is not { } standing)
        {
            return null;
        }

        return SceneReducer.Apply(
            scene,
            new TransformItem(
                transform.Item,
                transform.CenterX,
                transform.CenterY,
                transform.Scale,
                transform.RotationDeg,
                toFront ? Math.Max(standing.ZOrder, scene.Top(standing.Parked) + 1) : standing.ZOrder,
                standing.Revision),
            context);
    }
}
