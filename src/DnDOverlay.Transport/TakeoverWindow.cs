namespace DnDOverlay.Transport;

/// <summary>
/// Whether this display would answer a call for orphaned displays (Part 4). Open only when all
/// three hold:
/// <list type="bullet">
/// <item>it was connected in this run - or the ordinary state of an autostarting display, which has
/// no control most of the time, would be open;</item>
/// <item>that connection ended without a goodbye - <c>GoIdle</c> is the orderly end and closes the
/// window outright;</item>
/// <item>it ended longer ago than the rescue mark's grace period - a Wi-Fi hiccup must not make a
/// display answerable.</item>
/// </list>
/// <para>
/// <b>No grace period of its own</b>: the same clock and the same minute as the rescue mark, which
/// is where the display shows that it answered and where the answer can be taken back (Part 6).
/// So the window is open exactly when the mark is due - <see cref="IsOpen"/> answers both.
/// </para>
/// </summary>
public sealed class TakeoverWindow(TimeProvider time, TimeSpan? grace = null)
{
    /// <summary>The rescue mark's grace period: a minute without a control (Part 6).</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly HashSet<Guid> _vetoed = [];
    private TimeSpan _grace = grace ?? DefaultGrace;
    private bool _wasConnected;
    private bool _saidGoodbye;
    private long? _lostAt;

    /// <summary>
    /// The grace period, settable while the process runs - it is a device parameter, and a
    /// <c>ConfigUpdate</c> changes it (Part 6).
    /// </summary>
    public TimeSpan Grace
    {
        get
        {
            lock (_gate)
            {
                return _grace;
            }
        }

        set
        {
            lock (_gate)
            {
                _grace = value;
            }
        }
    }

    /// <summary>A connection stands - the window is shut while it does, and every veto is spent.</summary>
    public void Connected()
    {
        lock (_gate)
        {
            _wasConnected = true;
            _saidGoodbye = false;
            _lostAt = null;
            _vetoed.Clear();
        }
    }

    /// <summary>The connection ended without a goodbye; the grace period starts now.</summary>
    public void Lost()
    {
        lock (_gate)
        {
            if (_wasConnected && _lostAt is null)
            {
                _lostAt = time.GetTimestamp();
            }
        }
    }

    /// <summary>The control said goodbye (<c>GoIdle</c>): the window stays shut until the next connection.</summary>
    public void SaidGoodbye()
    {
        lock (_gate)
        {
            _saidGoodbye = true;
        }
    }

    /// <summary>
    /// The answer to this caller is taken back at the device - the rescue mark's second line, the
    /// veto and not the gate (Part 6). It holds until the next accepted connection: a caller that
    /// keeps calling for two minutes must not be answered again a moment later.
    /// </summary>
    public void Veto(Guid caller)
    {
        lock (_gate)
        {
            _vetoed.Add(caller);
        }
    }

    /// <summary>Whether a call from this control is answered right now: the window is open and nobody said no to it.</summary>
    public bool Answers(Guid caller)
    {
        lock (_gate)
        {
            return Open() && !_vetoed.Contains(caller);
        }
    }

    /// <summary>
    /// How long the connection has been gone, while it counts as missing - after a connection of this
    /// run that ended without a goodbye. <see langword="null"/> otherwise. What the rescue mark says:
    /// "No control for 3 min".
    /// </summary>
    public TimeSpan? Absent
    {
        get
        {
            lock (_gate)
            {
                return _wasConnected && !_saidGoodbye && _lostAt is { } lost ? time.GetElapsedTime(lost) : null;
            }
        }
    }

    /// <summary>Whether a call for orphaned displays is answered right now, and the rescue mark is due.</summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return Open();
            }
        }
    }

    private bool Open() =>
        _wasConnected
        && !_saidGoodbye
        && _lostAt is { } lost
        && time.GetElapsedTime(lost) >= _grace;
}
