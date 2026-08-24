namespace DnDOverlay.Core.Protocol;

/// <summary>
/// Rank 4, and the one shape it has: <b>a slot per kind, replaced rather than stacked</b>.
/// <para>
/// Part 4 gives each transient message a capacity of one - touch points per screen, diagnostics
/// and the window list per device. Two readings of the same kind never both wait, because the
/// older one is not inaccurate, it is worthless. What replacement means for the content is the
/// message's business (<see cref="IReplacing{T}"/>); that there IS a replacement is this type's.
/// </para>
/// <para>
/// <b>It stands once, and that is deliberate.</b> The same rule sits in front of
/// <c>/ws/display</c>, in front of the display's own socket and in front of a control's event
/// stream. A second copy of the discard rules would be the seam of M2 with another name -
/// <c>WebSocketFraming</c> stood twice, and the two ends drifted until one of them stopped
/// sending a token (M3, siting question 2).
/// </para>
/// <para>
/// Not thread safe by accident but on purpose: many callers offer, one takes, and both go through
/// the same lock. A dictionary is what a slot per kind needs, and a dictionary is what a channel
/// cannot be.
/// </para>
/// </summary>
/// <typeparam name="T">
/// What is queued - the protocol message in front of a socket, the session event in front of a
/// subscriber. What was transient coming from a device stays transient going to a second control
/// (Part 4).
/// </typeparam>
public sealed class TransientSlots<T>
    where T : class
{
    private readonly int _maxSlots;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    private readonly Dictionary<string, Waiting> _slots = [];

    /// <summary>
    /// Which slot goes next, in the order they first filled. A slot that is replaced keeps its
    /// place rather than going to the back: otherwise a screen with a busy hand on it would push
    /// a quieter one out of the way for as long as the hand stayed down.
    /// </summary>
    private readonly Queue<string> _order = new();

    /// <param name="maxSlots">
    /// How many kinds may wait at once. Bounded by the kinds this build has and the screens a
    /// device may report rather than by anything a counterpart controls, so this is a ceiling on
    /// a mistake of ours and not a defence.
    /// </param>
    public TransientSlots(int maxSlots, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSlots, 1);
        ArgumentNullException.ThrowIfNull(time);

        _maxSlots = maxSlots;
        _time = time;
    }

    /// <summary>Whether anything is waiting. Cheap, and only ever a hint - a taker still decides.</summary>
    public bool Any
    {
        get
        {
            lock (_gate)
            {
                return _slots.Count > 0;
            }
        }
    }

    /// <summary>
    /// Puts an item in its slot, over whatever was already there.
    /// <para>
    /// Returns <see langword="false"/> only when there is no slot left to open - which is a fault
    /// in this build rather than a busy moment, and is answered the way rank 4 answers everything:
    /// by dropping.
    /// </para>
    /// </summary>
    public bool Offer(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var key = Key(item);

        lock (_gate)
        {
            if (_slots.TryGetValue(key, out var waiting))
            {
                // How much older the waiting one is. The merged item then lies on THIS item's
                // clock, which is why the stamp is taken again below rather than carried over.
                var gapMs = Elapsed(waiting.At);

                _slots[key] = new Waiting(
                    item is IReplacing<T> replacing ? replacing.Over(waiting.Item, gapMs) : item,
                    _time.GetTimestamp());

                return true;
            }

            if (_slots.Count >= _maxSlots)
            {
                return false;
            }

            _slots[key] = new Waiting(item, _time.GetTimestamp());
            _order.Enqueue(key);

            return true;
        }
    }

    /// <summary>
    /// The next thing worth sending, with what the wait cost already accounted for - skipping
    /// anything the wait has made worthless.
    /// </summary>
    public bool TryTake(out T? item)
    {
        lock (_gate)
        {
            while (_order.TryDequeue(out var key))
            {
                if (!_slots.Remove(key, out var waiting))
                {
                    continue;
                }

                var ready = waiting.Item is IReplacing<T> replacing
                    ? replacing.Sent(Elapsed(waiting.At))
                    : waiting.Item;

                if (ready is null)
                {
                    // Too old to be worth a wire. Said nowhere: this is ordinary operation and
                    // not an incident, and a line per drop at ten a second would bury the log.
                    continue;
                }

                item = ready;

                return true;
            }
        }

        item = null;

        return false;
    }

    /// <summary>
    /// A message that does not say which slot it belongs in gets one per type, which is the same
    /// answer Part 4 gives for the three that do - and the right default for whatever is added
    /// next without thinking about it.
    /// </summary>
    private static string Key(T item) =>
        item is IReplacing<T> replacing ? replacing.Slot : item.GetType().Name;

    private int Elapsed(long since) => (int)_time.GetElapsedTime(since).TotalMilliseconds;

    private readonly record struct Waiting(T Item, long At);
}
