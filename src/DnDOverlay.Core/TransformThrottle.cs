namespace DnDOverlay.Core;

/// <summary>
/// How often a running gesture is reported: about twenty times a second per ITEM, and once more,
/// bindingly, when the fingers leave (Part 4).
/// <para>
/// <b>Per item and not globally</b>, because two pictures moved at once would otherwise slow each
/// other down - at a table where two players each push their own picture, a global limit would
/// halve both. That is the whole reason this is a table of items rather than one timestamp.
/// </para>
/// <para>
/// <b>And it throttles BEFORE the queue, not inside it.</b> Throttling is a decision about how much
/// detail a movement needs; dropping is an emergency measure for a socket that cannot keep up
/// (Part 4). Were this left to the transient queue, the binding final report would be exactly the
/// message most likely to be thrown away - and that one is the difference between a picture that
/// stays where it was let go and one that snaps back.
/// </para>
/// <para>
/// It is a decision and therefore lives here, where it can be asserted, rather than as a
/// comparison against a clock somewhere in a window's event handler.
/// </para>
/// </summary>
public sealed class TransformThrottle
{
    /// <summary>~20 Hz (Part 4). Anything rarer looks like the picture is lagging behind the finger.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    private readonly Dictionary<ItemId, long> _last = [];
    private readonly long _intervalMs;

    public TransformThrottle(TimeSpan? interval = null) =>
        _intervalMs = (long)(interval ?? DefaultInterval).TotalMilliseconds;

    /// <summary>
    /// Whether this report goes out. A <paramref name="binding"/> one always does and ends the
    /// gesture's memory - it is the report the scene is left standing on.
    /// </summary>
    /// <param name="nowMs">
    /// A monotonic reading in milliseconds, handed in rather than read here: Core has no clock, and
    /// a gesture is exactly the place where a wall clock stepping backwards would freeze the
    /// reporting for the length of the step (rule 10).
    /// </param>
    public bool Allows(ItemId item, long nowMs, bool binding)
    {
        if (binding)
        {
            _last.Remove(item);

            return true;
        }

        if (_last.TryGetValue(item, out var last) && nowMs - last < _intervalMs)
        {
            return false;
        }

        _last[item] = nowMs;

        return true;
    }

    /// <summary>
    /// Forgets an item whose gesture ended without a binding report - a picture that vanished under
    /// the finger because a snapshot no longer carried it (Part 4, conflict rule 4). Without this
    /// the table would keep one entry per such item for as long as the process runs.
    /// </summary>
    public void Forget(ItemId item) => _last.Remove(item);
}
