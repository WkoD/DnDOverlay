using System.Collections.Concurrent;
using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// The gestures this device has sent and the hub has not answered yet: where each picture was
/// taken hold of, and where it was let go and when. It tells the hub's late answer to this device's
/// own grab from a correction, and it is what the answer to a release is measured against
/// (<c>3028</c>).
/// <para>
/// <b>Told apart by what an answer says, not by where it came from</b>, because a patch carries no
/// sender (Guide F9). Anything that is not exactly this device's own place - the DM moving the
/// picture, the hub clamping it - is a correction and wins, as conflict rule 2 says (Part 4).
/// </para>
/// <para>
/// Written on the UI thread where a hand lets go, read on the connection task where the patch is
/// applied - concurrent for both.
/// </para>
/// </summary>
internal sealed class OwnGestures
{
    /// <summary>
    /// Below half a device-independent pixel the two ends agree. Anything a hand or an eye could
    /// notice is above it by a wide margin.
    /// </summary>
    internal const double DriftWorthSaying = 0.5;

    /// <summary>
    /// How many releases of one picture are kept unanswered. Bounded, because an operation the hub
    /// never answers - the item was gone, or locked - would otherwise leave an entry that every later
    /// answer is measured against.
    /// </summary>
    internal const int MaxUnanswered = 16;

    /// <summary>
    /// The releases per item, with the place they were let go at and the moment they were sent.
    /// <para>
    /// <b>A queue per item, not one slot</b>, and the first run of this line is why: it kept one
    /// entry per picture, a second push replaced the first, and the answer to the first was then
    /// measured against the second's place. It reported drifts of up to 918 DIP where the two ends
    /// in fact agreed - the instrument's own fault, and exactly the sort of number that would have
    /// been chased for an evening. Answers come back in the order they were sent, so the oldest
    /// entry is the one being answered.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<ItemId, ConcurrentQueue<(ItemTransform Where, long AtMs)>> _awaiting = new();

    /// <summary>Where each picture was when this device took hold of it, until the hub has answered that grab.</summary>
    private readonly ConcurrentDictionary<ItemId, ItemTransform> _grabbedAt = new();

    /// <summary>A hand took hold of a picture here: the place its grab carried.</summary>
    internal void Grabbed(ItemTransform at)
    {
        ArgumentNullException.ThrowIfNull(at);

        _grabbedAt[at.Item] = at;
    }

    /// <summary>A hand let go here: the binding place, and when it was sent.</summary>
    internal void LetGo(ItemTransform at, long atMs)
    {
        ArgumentNullException.ThrowIfNull(at);

        var queue = _awaiting.GetOrAdd(at.Item, _ => new ConcurrentQueue<(ItemTransform, long)>());

        while (queue.Count >= MaxUnanswered)
        {
            _ = queue.TryDequeue(out _);
        }

        queue.Enqueue((at, atMs));
    }

    /// <summary>
    /// The picture went away: its grab will never be answered. Its releases stay - they are bounded
    /// by <see cref="MaxUnanswered"/>.
    /// </summary>
    internal void Removed(ItemId item) => _grabbedAt.TryRemove(item, out _);

    /// <summary>
    /// The hub's answer to this device's own GRAB, arriving after the hand has already let go -
    /// returned as depth only, with this device's own place kept; <see langword="null"/> for every
    /// other operation, which is applied as it comes.
    /// <para>
    /// <b>Found at a game session (18.10.2026), and it is a picture jumping back.</b> The hub answers
    /// a grab with the place the picture had when it was taken - it has to, the operation carries
    /// one - and sends that answer back to the grabbing device, because it carries the binding
    /// depth. On a quiet link it arrives while the hand is still on the picture and the hold keeps
    /// the finger's geometry. While the table was loading a 32 MB picture over Wi-Fi it arrived
    /// 1.6 s late, after the release: the picture sprang back to where it had arrived, and forward
    /// again when the answer to the release came round.
    /// </para>
    /// <para>
    /// An answer to the grab is recognised once: it names exactly the place this device sent with its
    /// grab, for a picture whose release is still unanswered.
    /// </para>
    /// </summary>
    internal TransformItem? LateAnswerToGrab(TransformItem moved, SceneState scene, ScreenContext context)
    {
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(context);

        if (!_grabbedAt.TryGetValue(moved.Item, out var grab) || !SamePlace(moved, grab, context))
        {
            // Somebody else's move, or the hub's own clamping: a correction, applied as it is
            // (conflict rule 2).
            return null;
        }

        _ = _grabbedAt.TryRemove(moved.Item, out _);

        // Nothing let go yet: the answer is in time, and the hold keeps the finger's geometry anyway.
        if (!_awaiting.TryGetValue(moved.Item, out var released) || released.IsEmpty
            || scene.Items.FirstOrDefault(item => item.ItemId == moved.Item) is not { } mine)
        {
            return null;
        }

        return moved with
        {
            CenterX = mine.CenterX,
            CenterY = mine.CenterY,
            Scale = mine.Scale,
            RotationDeg = mine.RotationDeg,
        };
    }

    /// <summary>
    /// What the hub made of the oldest release of this picture still unanswered - once, for the
    /// answer that was actually waited for; <see langword="null"/> for an operation on an item
    /// nobody here let go of, which is a stranger's and passes without a word.
    /// </summary>
    internal Answer? Answered(TransformItem moved, ScreenContext context, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(context);

        if (!_awaiting.TryGetValue(moved.Item, out var queue) || !queue.TryDequeue(out var mine))
        {
            return null;
        }

        // In DIP rather than in the normalised fractions, because a thousandth of a screen means
        // nothing to a reader and two pixels do. The two axes are separate fractions of two
        // different edges, so each is converted with its own (Part 3).
        var dx = (moved.CenterX - mine.Where.CenterX) * context.Size.Width;
        var dy = (moved.CenterY - mine.Where.CenterY) * context.Size.Height;

        return new Answer(mine.Where, Math.Round(Math.Sqrt((dx * dx) + (dy * dy)), 1), nowMs - mine.AtMs);
    }

    /// <summary>
    /// Whether an answer carries the very place a report sent - within the half pixel that counts
    /// as agreement everywhere else here.
    /// </summary>
    private static bool SamePlace(TransformItem answer, ItemTransform sent, ScreenContext context)
    {
        var dx = (answer.CenterX - sent.CenterX) * context.Size.Width;
        var dy = (answer.CenterY - sent.CenterY) * context.Size.Height;

        return Math.Sqrt((dx * dx) + (dy * dy)) <= DriftWorthSaying
            && Math.Abs(answer.Scale - sent.Scale) < 0.0005
            && Math.Abs(answer.RotationDeg - sent.RotationDeg) < 0.05;
    }

    /// <summary>The hub's answer to one release.</summary>
    /// <param name="Sent">Where this device let go.</param>
    /// <param name="DriftDip">How far the answer lies from it, rounded to a tenth.</param>
    /// <param name="SpentMs">How long the answer took.</param>
    internal sealed record Answer(ItemTransform Sent, double DriftDip, long SpentMs)
    {
        /// <summary>
        /// Half a pixel is agreement. The values travel as doubles and come back through the same
        /// arithmetic on both ends, so the ordinary case is exactly nothing - measured over an
        /// evening, 120 of 123 gestures came back at 0.
        /// </summary>
        internal bool Agrees => DriftDip <= DriftWorthSaying;
    }
}
