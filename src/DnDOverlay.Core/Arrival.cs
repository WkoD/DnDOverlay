namespace DnDOverlay.Core;

/// <summary>
/// Which pictures count as having just ARRIVED, and therefore light up briefly.
/// <para>
/// The reason is the players' side: on a table holding twelve pictures a thirteenth appears
/// somewhere in the flow order and <b>nobody notices it</b>. The DM then says "look at the table",
/// which undoes the point of showing it. Sound is not available as a channel (Part 1), so the
/// picture has to draw attention to itself (Part 6).
/// </para>
/// <para>
/// <b>The whole difficulty is in the word "new".</b> Done naively - the item was not there a moment
/// ago - the entire table lights up after every reconnect, after every snapshot and after every
/// scene change, and a hint that fires constantly stops meaning anything. So the question is asked
/// of a PATCH and never of two states: a snapshot is not a patch and therefore never marks
/// anything, which is exactly the wanted answer for the restore after a restart and for the first
/// fill after connecting.
/// </para>
/// <para>
/// It lives here rather than in the display because the thumbnail shows the same highlight, with
/// the same rule and the same duration (Part 6), and two implementations of "what is new" would
/// disagree the first time one of them was fixed.
/// </para>
/// </summary>
public static class Arrival
{
    /// <summary>
    /// The items this patch brings onto a screen that was already occupied.
    /// <para>
    /// Three conditions, and each one is a case from Part 11's list of exceptions:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>The scene was standing.</b> A first picture on an empty screen is not lost among others,
    /// so nothing has to point at it - and "an <c>AddItem</c> into an already standing scene" is
    /// how the rule is written (Part 6).
    /// </item>
    /// <item>
    /// <b>The patch does nothing but add.</b> That is what tells the two kinds of scene loading
    /// apart without either of them being named here: <i>add</i> is a patch of nothing but
    /// <c>AddItem</c> and marks what came, while <i>replace</i> carries a clearing operation and
    /// therefore marks nothing. The same clause covers "empty the lot" and every other command that
    /// happens to add something on the way.
    /// </item>
    /// <item>
    /// <b>The id was not already there.</b> A patch delivered twice after a reconnect would
    /// otherwise light up a picture that has been lying there all evening.
    /// </item>
    /// </list>
    /// <para>
    /// <b>A picture that arrives by being moved from another screen DOES light up</b>, and that
    /// follows from the same clause rather than from an exception: on the target screen such a patch
    /// is a plain <c>AddItem</c>. It is also the right answer - for the players at that table the
    /// picture is new, and where the DM took it from is not their question.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ItemId> Marked(SceneState before, IReadOnlyList<PatchOp> ops)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(ops);

        if (before.Items.Count == 0 || ops.Count == 0 || ops.Any(op => op is not AddItem))
        {
            return [];
        }

        var standing = before.Items.Select(item => item.ItemId).ToHashSet();

        return
        [
            .. ops.OfType<AddItem>()
                .Select(op => op.Item.ItemId)
                .Where(id => !standing.Contains(id)),
        ];
    }
}
