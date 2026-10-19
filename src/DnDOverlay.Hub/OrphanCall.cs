namespace DnDOverlay.Hub;

/// <summary>
/// The call for orphaned displays: set for two minutes when the DM asks for it, and carried in the
/// beacon while it lasts (Part 4).
/// <para>
/// <b>Nothing happens without somebody pressing it, and that is the point.</b> A pure time rule
/// would offer the displays of one control to a second, legitimate one after a minute, and a slip
/// of the hand there gives the table to the wrong machine. The call has to be sent on purpose, by
/// the person at the control whose identity is gone.
/// </para>
/// </summary>
public sealed class OrphanCall(TimeProvider time)
{
    /// <summary>
    /// Long enough for the backoff of a display that is just waiting thirty seconds, short enough
    /// that nobody forgets it is running (Part 4).
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(2);

    private readonly Lock _gate = new();
    private long? _since;

    /// <summary>Starts the call, or starts it again from now.</summary>
    public void Start()
    {
        lock (_gate)
        {
            _since = time.GetTimestamp();
        }
    }

    /// <summary>Whether the beacon carries the call right now.</summary>
    public bool Calling
    {
        get
        {
            lock (_gate)
            {
                return _since is { } since && time.GetElapsedTime(since) < Duration;
            }
        }
    }
}
