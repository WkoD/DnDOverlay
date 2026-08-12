using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Logging;

/// <summary>
/// The last few hundred messages, in memory.
/// <para>
/// It exists for the moment nothing can be sent: the most interesting messages of all are the
/// ones that come up while the connection is down, and they are pushed after it comes back
/// (Part 8). It also feeds the tray list at the device and the log panel in the control - one
/// buffer, three readers.
/// </para>
/// <para>
/// A reader holds a <b>mark</b>, not a position: it asks for everything after what it last saw.
/// That is what makes "push what was missed" a property rather than bookkeeping, and it lets two
/// readers - the panel and the forwarding - run at different speeds without taking entries away
/// from one another.
/// </para>
/// </summary>
public sealed class LogRing
{
    /// <summary>
    /// Enough for a long outage at Information and a short one at Debug. Bounded on purpose: this
    /// is a buffer, not a second log file, and what falls out of it is on disk anyway.
    /// </summary>
    public const int DefaultCapacity = 512;

    private readonly Lock _gate = new();
    private readonly LogRecord?[] _slots;

    private long _next;

    public LogRing(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _slots = new LogRecord?[capacity];
    }

    /// <summary>The mark a reader starting now would hold: everything before it is history.</summary>
    public long Mark
    {
        get
        {
            lock (_gate)
            {
                return _next;
            }
        }
    }

    public void Add(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            _slots[(int)(_next % _slots.Length)] = record;
            _next++;
        }
    }

    /// <summary>The newest entries, oldest first - what the tray list and the panel show.</summary>
    public IReadOnlyList<LogRecord> Recent(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        lock (_gate)
        {
            var found = new List<LogRecord>();

            for (var sequence = Math.Max(Oldest(), _next - count); sequence < _next; sequence++)
            {
                if (_slots[(int)(sequence % _slots.Length)] is { } record)
                {
                    found.Add(record);
                }
            }

            return found;
        }
    }

    /// <summary>
    /// Everything after <paramref name="mark"/> that is at least <paramref name="atLeast"/>, and
    /// where the reader stands afterwards.
    /// </summary>
    /// <param name="next">
    /// The new mark. It moves past everything LOOKED AT, not only past what was returned -
    /// otherwise entries below the level would be walked again on every pass, for ever.
    /// </param>
    /// <param name="lost">
    /// How many entries fell out of the buffer before this reader got to them. Counted per reader
    /// rather than globally, because two readers at different speeds lose different amounts - and
    /// a number that is silently wrong is worse than none.
    /// </param>
    public IReadOnlyList<LogRecord> Since(long mark, LogLevel atLeast, int max, out long next, out int lost)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);

        lock (_gate)
        {
            var oldest = Oldest();

            lost = mark < oldest ? (int)(oldest - mark) : 0;

            var sequence = Math.Max(mark, oldest);
            var found = new List<LogRecord>();

            while (sequence < _next && found.Count < max)
            {
                if (_slots[(int)(sequence % _slots.Length)] is { } record && record.Level >= atLeast)
                {
                    found.Add(record);
                }

                sequence++;
            }

            next = sequence;

            return found;
        }
    }

    private long Oldest() => Math.Max(0, _next - _slots.Length);
}
