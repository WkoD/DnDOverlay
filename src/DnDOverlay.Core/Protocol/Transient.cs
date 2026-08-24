namespace DnDOverlay.Core.Protocol;

/// <summary>
/// A transient item that <b>replaces</b> the one still waiting in its slot instead of queueing
/// behind it - and says what becomes of the one it displaces.
/// <para>
/// The three transient messages of Part 4 each get a slot of one: <c>TouchPoints</c> per screen,
/// <c>Diagnostics</c> and <c>WindowList</c> per device. Two readings of the same kind never both
/// wait, because the older one is not inaccurate - it is worthless.
/// </para>
/// <para>
/// <b>Where the decision lives, and where the knowledge does.</b> Only the queue knows what is
/// still unsent, so only the queue can decide to replace; only the message knows what its own
/// content means, so only the message can say how two of them combine and what a wait costs
/// them. Splitting it here keeps the queue free of any notion of a finger and the message free
/// of any notion of a socket.
/// </para>
/// </summary>
/// <typeparam name="T">
/// What the queue carries - <see cref="ProtocolMessage"/> in front of a socket, and the session
/// event in front of a subscriber. The rule is the same on both, which is the whole point of
/// stating it once (Part 4).
/// </typeparam>
public interface IReplacing<T>
    where T : class
{
    /// <summary>
    /// Which slot this occupies. Two items with the same key never both wait; a different key is
    /// a different kind of reading and has a slot of its own.
    /// </summary>
    string Slot { get; }

    /// <summary>
    /// This item laid over the one it displaces, which was made <paramref name="gapMs"/>
    /// milliseconds earlier.
    /// <para>
    /// For most transients the answer is <c>this</c> - a frame time from a moment ago says nothing
    /// the newer one does not. For <c>TouchPoints</c> it is not: overwriting would drop exactly
    /// the points that make the line. <b>Discarding the delay is allowed, discarding the movement
    /// is not</b> (Part 4).
    /// </para>
    /// </summary>
    T Over(T waiting, int gapMs);

    /// <summary>
    /// This item as it should go out after <paramref name="waitedMs"/> in the queue, or
    /// <see langword="null"/> when the wait made it worthless.
    /// <para>
    /// Both halves belong to the message rather than to the queue: what a wait costs depends on
    /// what the content means. A trail carries ages relative to the moment it is sent, so a wait
    /// moves every one of them; a frame time carries none, so a wait moves nothing.
    /// </para>
    /// </summary>
    T? Sent(int waitedMs);
}
