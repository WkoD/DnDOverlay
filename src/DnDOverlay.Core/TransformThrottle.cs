namespace DnDOverlay.Core;

/// <summary>What kind of report a gesture is making, which decides whether it may wait.</summary>
public enum ReportKind
{
    /// <summary>A step in the middle of a gesture. It may be held back and sent late.</summary>
    Step,

    /// <summary>
    /// A moment the other end must not miss - the grab, a hand leaving its tile or coming back. It
    /// goes out at once and takes the place of any step still waiting.
    /// </summary>
    Urgent,

    /// <summary>
    /// The last report of a gesture, the one the scene is left standing on. It goes out at once,
    /// cancels any step still waiting, and ends the gesture's memory.
    /// </summary>
    Final,
}

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
/// <b>A step that arrives inside the interval is kept, not thrown away, and sent when the interval
/// ends</b> (sixth hand-run of M4, 15.09.2026). It used to be thrown away with nothing trailing: a
/// hand that stopped without letting go left the other end one dropped step behind until it moved
/// again, and a hand leaving a tile in the thumbnail took the last steps with it, so the table
/// stopped short of the tile - the further the faster the hand. Only the LATEST held step is sent;
/// the ones before it are superseded, which is still throttling.
/// </para>
/// <para>
/// <b>Nothing held may arrive after the report that ends it</b>, and that is the one rule this
/// class exists to keep. A late step arriving at the hub after the final report would move the
/// picture back - and not only at the sender, because the hub hands the middle of a gesture to
/// everyone else (conflict rule 2). So an urgent or final report cancels the held step and is sent
/// <b>inside the same lock</b> the timer takes to send one: whichever runs first, the order in
/// which the two sends are MADE is the order in which they arrive, on the hub's gate and on a
/// display's queue alike. A wait that was cancelled carries a mark, and a timer that fires anyway
/// finds its mark outdated and sends nothing.
/// </para>
/// <para>
/// <b>The send happens on a timer's thread</b> when it is a held step. That is why a report hands
/// over a finished action with its values already taken, rather than something that reads the
/// window's gesture state when it runs.
/// </para>
/// </summary>
public sealed class TransformThrottle
{
    /// <summary>~20 Hz (Part 4). Anything rarer looks like the picture is lagging behind the finger.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly Dictionary<ItemId, Slot> _slots = [];
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;

    /// <param name="time">
    /// Handed in rather than read: the libraries do not read a clock of their own (rule 10), and a
    /// test has to be able to move time on to see a held step go out.
    /// </param>
    /// <param name="interval">How long a step waits at most. <see cref="DefaultInterval"/> if not given.</param>
    public TransformThrottle(TimeProvider? time = null, TimeSpan? interval = null)
    {
        _time = time ?? TimeProvider.System;
        _interval = interval ?? DefaultInterval;
    }

    /// <summary>
    /// Offers one report of a gesture. <paramref name="send"/> is carried out now, later from a
    /// timer, or never because a newer report took its place.
    /// </summary>
    public void Report(ItemId item, ReportKind kind, Action send)
    {
        ArgumentNullException.ThrowIfNull(send);

        lock (_gate)
        {
            var now = _time.GetTimestamp();

            if (kind is ReportKind.Final)
            {
                // REMOVING the entry is what keeps a held step from following: a timer that fires
                // later finds no entry, or a newer one, and sends nothing. Cancelling only frees the
                // timer - taken out on its own it fails no test, and that was measured, not assumed.
                if (_slots.Remove(item, out var ended))
                {
                    ended.Cancel();
                }

                send();

                return;
            }

            if (!_slots.TryGetValue(item, out var slot))
            {
                slot = new Slot();
                _slots[item] = slot;
            }

            if (kind is ReportKind.Urgent
                || slot.Last is not { } last
                || _time.GetElapsedTime(last, now) >= _interval)
            {
                slot.Cancel();
                slot.Last = now;

                send();

                return;
            }

            // Inside the interval: kept, replacing whatever was kept before it.
            slot.Pending = send;

            if (slot.Timer is null)
            {
                var mark = ++slot.Mark;

                slot.Timer = _time.CreateTimer(
                    Fire,
                    new Wait(item, slot, mark),
                    _interval - _time.GetElapsedTime(last, now),
                    Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Forgets an item whose gesture ended without a final report - a picture put into the fan, or
    /// one that vanished under the finger because a snapshot no longer carried it (Part 4, conflict
    /// rule 4). A step still waiting for it is dropped: the item it would move is gone or put away.
    /// </summary>
    public void Forget(ItemId item)
    {
        lock (_gate)
        {
            if (_slots.Remove(item, out var slot))
            {
                slot.Cancel();
            }
        }
    }

    private void Fire(object? state)
    {
        var wait = (Wait)state!;

        lock (_gate)
        {
            // The mark is what makes a late timer harmless: a wait that was cancelled, or replaced
            // by a newer one, no longer matches, whatever the slot holds by now.
            if (!_slots.TryGetValue(wait.Item, out var current)
                || !ReferenceEquals(current, wait.Slot)
                || current.Mark != wait.Mark
                || current.Pending is not { } send)
            {
                return;
            }

            current.Timer?.Dispose();
            current.Timer = null;
            current.Pending = null;
            current.Last = _time.GetTimestamp();

            send();
        }
    }

    private sealed record Wait(ItemId Item, Slot Slot, long Mark);

    /// <summary>What one item's gesture remembers between two reports.</summary>
    private sealed class Slot
    {
        internal long? Last { get; set; }

        internal Action? Pending { get; set; }

        internal ITimer? Timer { get; set; }

        internal long Mark { get; set; }

        /// <summary>Drops the held step and outdates the wait for it.</summary>
        internal void Cancel()
        {
            Pending = null;
            Timer?.Dispose();
            Timer = null;
            Mark++;
        }
    }
}
