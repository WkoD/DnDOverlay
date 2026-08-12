namespace DnDOverlay.Core.Tests.Configuration;

/// <summary>
/// A clock that only moves when a test moves it.
/// <para>
/// This is what handing the clock in buys (rule 10): a debounce of two seconds can be checked
/// in microseconds, and the check is exact rather than "probably long enough". A test that
/// slept would be slow AND flaky - the two failure modes of timing tests at once.
/// </para>
/// </summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];

    private DateTimeOffset _now = new(2026, 8, 11, 18, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <summary>
    /// The monotonic reading follows this clock too, and it has to.
    /// <para>
    /// <see cref="TimeProvider"/> leaves <see cref="GetTimestamp"/> on the real stopwatch by
    /// default, so anything measuring an ELAPSED time - the heartbeat's silence deadline, a round
    /// trip - would quietly ignore <see cref="Advance"/> and the test would prove nothing while
    /// staying green. Production code is right to use the monotonic reading rather than the wall
    /// clock; this is what makes that choice testable.
    /// </para>
    /// </summary>
    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _now.UtcTicks;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);

        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);

        return timer;
    }

    /// <summary>Moves the clock and fires whatever came due on the way.</summary>
    public void Advance(TimeSpan by)
    {
        ManualTimer[] timers;

        lock (_gate)
        {
            _now += by;
            timers = [.. _timers];
        }

        // Outside the lock: a callback writes a file and takes locks of its own.
        foreach (var timer in timers)
        {
            timer.FireIfDue(GetUtcNow());
        }
    }

    private void Forget(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        private readonly Lock _gate = new();

        private DateTimeOffset? _due;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : time.GetUtcNow() + dueTime;
                _period = period;
            }

            return true;
        }

        public void FireIfDue(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_due is null || _due > now)
                {
                    return;
                }

                _due = _period == Timeout.InfiniteTimeSpan ? null : now + _period;
            }

            callback(state);
        }

        public void Dispose() => time.Forget(this);

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
