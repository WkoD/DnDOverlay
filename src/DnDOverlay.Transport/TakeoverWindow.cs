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
/// </para>
/// </summary>
public sealed class TakeoverWindow(TimeProvider time, TimeSpan? grace = null)
{
    /// <summary>The rescue mark's grace period: a minute without a control (Part 6).</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly TimeSpan _grace = grace ?? DefaultGrace;
    private bool _wasConnected;
    private bool _saidGoodbye;
    private long? _lostAt;

    /// <summary>A connection stands - the window is shut while it does.</summary>
    public void Connected()
    {
        lock (_gate)
        {
            _wasConnected = true;
            _saidGoodbye = false;
            _lostAt = null;
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

    /// <summary>Whether a call for orphaned displays is answered right now.</summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _wasConnected
                    && !_saidGoodbye
                    && _lostAt is { } lost
                    && time.GetElapsedTime(lost) >= _grace;
            }
        }
    }
}
