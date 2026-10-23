using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// Whether anybody answered the call for orphaned devices (Part 4) - so that the window can say so
/// when the call is over and nobody did (Prüfschritt 43c, the veto: "das Control sagt, dass niemand
/// geantwortet hat").
/// <para>
/// <b>An answer is a request that appears under "Waiting for a decision" while the call runs.</b> A
/// display answers with its ordinary <c>Hello</c>, and the hub turns that into a pairing request
/// (Part 4) - so the request list is the one place the window sees an answer, and the only one it
/// needs. A request that was waiting before the call is no answer to it; one that went away and came
/// back during the call knocked again, and is.
/// </para>
/// <para>
/// <b>Each press is a call of its own.</b> Pressing again starts the two minutes anew (Part 4), so
/// the ending of an earlier press is passed over - it would report on a call that is still running.
/// The watch lives with the window: closed and opened again, the window knows of no call, and
/// nothing stale is said.
/// </para>
/// </summary>
internal sealed class OrphanWatch
{
    private HashSet<DeviceId>? _before;
    private bool _answered;
    private int _call;

    /// <summary>How a call ended, as far as the window has anything to say about it.</summary>
    internal enum Ending
    {
        /// <summary>Nothing to say: this call was overtaken by a later press, or no call ran.</summary>
        None,

        /// <summary>A request appeared during the call - it stands in the list.</summary>
        Answered,

        /// <summary>The call ran out and no request appeared.</summary>
        NobodyAnswered,
    }

    /// <summary>A call began, with these requests already waiting. The number names this call for <see cref="Ended"/>.</summary>
    internal int Started(IEnumerable<DeviceId> waiting)
    {
        _before = [.. waiting];
        _answered = false;

        return ++_call;
    }

    /// <summary>The requests waiting now.</summary>
    internal void Saw(IEnumerable<DeviceId> waiting)
    {
        if (_before is not { } before)
        {
            return;
        }

        var now = waiting.ToHashSet();

        if (now.Any(device => !before.Contains(device)))
        {
            _answered = true;
        }

        // A request that went away is no longer "there before": should it come back while the call
        // runs, it knocked again.
        before.IntersectWith(now);
    }

    /// <summary>The time of this call is up.</summary>
    internal Ending Ended(int call)
    {
        if (call != _call || _before is null)
        {
            return Ending.None;
        }

        _before = null;

        return _answered ? Ending.Answered : Ending.NobodyAnswered;
    }
}
