using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// How often a running gesture is reported. It is a decision and not a mechanism, which is why it
/// is here and not a comparison against a clock inside an event handler (Part 4).
/// </summary>
public sealed class TransformThrottleTests
{
    private static readonly ItemId One = new(Guid.Parse("11111111-0000-0000-0000-000000000001"));
    private static readonly ItemId Two = new(Guid.Parse("11111111-0000-0000-0000-000000000002"));

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private readonly ManualTime _time = new();
    private readonly List<string> _sent = [];

    private TransformThrottle Throttle() => new(_time, Interval);

    private Action Send(string what) => () => _sent.Add(what);

    [Fact]
    public void The_first_report_of_a_gesture_goes_out()
    {
        Throttle().Report(One, ReportKind.Step, Send("a"));

        Assert.Equal(["a"], _sent);
    }

    /// <summary>
    /// <b>A step inside the interval is kept and goes out when the interval ends</b> - not thrown away.
    /// Thrown away, a hand that stopped without letting go left the other end a step behind until it
    /// moved again (sixth hand-run of M4, 15.09.2026).
    /// </summary>
    [Fact]
    public void A_step_inside_the_interval_is_held_and_sent_when_the_interval_ends()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("a"));
        _time.Advance(TimeSpan.FromMilliseconds(20));
        throttle.Report(One, ReportKind.Step, Send("b"));

        Assert.Equal(["a"], _sent);

        _time.Advance(TimeSpan.FromMilliseconds(29));

        Assert.Equal(["a"], _sent);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(["a", "b"], _sent);
    }

    /// <summary>Only the latest held step: the ones before it are superseded, which is still throttling.</summary>
    [Fact]
    public void Only_the_latest_held_step_goes_out()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("a"));
        throttle.Report(One, ReportKind.Step, Send("b"));
        throttle.Report(One, ReportKind.Step, Send("c"));
        throttle.Report(One, ReportKind.Step, Send("d"));
        _time.Advance(Interval);

        Assert.Equal(["a", "d"], _sent);
    }

    /// <summary>
    /// <b>The rule the whole class keeps.</b> A held step that arrived after the final report would put
    /// the picture back - at every display, because the hub hands the middle of a gesture to everyone
    /// but its sender (conflict rule 2).
    /// </summary>
    [Fact]
    public void Nothing_held_goes_out_after_the_final_report()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("a"));
        throttle.Report(One, ReportKind.Step, Send("held"));
        throttle.Report(One, ReportKind.Final, Send("final"));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(["a", "final"], _sent);
    }

    /// <summary>
    /// An urgent report takes the held step's place and goes out at once - leaving a tile, coming back
    /// to it. The held step is older than it and must not follow.
    /// </summary>
    [Fact]
    public void An_urgent_report_goes_out_at_once_and_the_held_step_does_not_follow()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("a"));
        throttle.Report(One, ReportKind.Step, Send("held"));
        throttle.Report(One, ReportKind.Urgent, Send("urgent"));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(["a", "urgent"], _sent);
    }

    /// <summary>
    /// After a held step went out, the cadence goes on: the next step inside the interval waits again.
    /// Without this the trailing report would turn into a second, unthrottled stream.
    /// </summary>
    [Fact]
    public void After_a_held_step_went_out_the_next_one_waits_again()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("a"));
        throttle.Report(One, ReportKind.Step, Send("b"));
        _time.Advance(Interval);
        throttle.Report(One, ReportKind.Step, Send("c"));

        Assert.Equal(["a", "b"], _sent);

        _time.Advance(Interval);

        Assert.Equal(["a", "b", "c"], _sent);
    }

    /// <summary>
    /// <b>Per item, and this is the test that says why.</b> Two players each pushing their own picture
    /// must not halve each other's reporting - a global limit would do exactly that (Part 4).
    /// </summary>
    [Fact]
    public void Two_pictures_moved_at_once_do_not_slow_each_other_down()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("one"));
        throttle.Report(Two, ReportKind.Step, Send("two"));
        throttle.Report(One, ReportKind.Step, Send("one held"));
        throttle.Report(Two, ReportKind.Final, Send("two final"));
        _time.Advance(Interval);

        Assert.Equal(["one", "two", "two final", "one held"], _sent);
    }

    /// <summary>
    /// A new gesture after a final report starts with a clean slate rather than inside the interval.
    /// </summary>
    [Fact]
    public void A_new_gesture_after_a_final_report_reports_at_once()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("a"));
        throttle.Report(One, ReportKind.Final, Send("final"));
        throttle.Report(One, ReportKind.Step, Send("next"));

        Assert.Equal(["a", "final", "next"], _sent);
    }

    /// <summary>
    /// A picture put into the fan or gone from under the finger never sends its final report. Forgotten,
    /// its held step is dropped with it - the item it would move is put away or gone.
    /// </summary>
    [Fact]
    public void A_forgotten_item_sends_nothing_it_was_holding()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("a"));
        throttle.Report(One, ReportKind.Step, Send("held"));
        throttle.Forget(One);
        _time.Advance(TimeSpan.FromSeconds(1));
        throttle.Report(One, ReportKind.Step, Send("next"));

        Assert.Equal(["a", "next"], _sent);
    }

    /// <summary>
    /// <b>A timer that fires after its wait was cancelled sends nothing</b>, even if a newer step is held
    /// by then. Disposing a timer does not stop a callback that is already on its way, and without the
    /// mark such a callback would send the NEWER step before its interval was up.
    /// </summary>
    [Fact]
    public void A_late_timer_from_a_cancelled_wait_does_not_send_a_newer_step_early()
    {
        var throttle = Throttle();

        throttle.Report(One, ReportKind.Step, Send("a"));
        throttle.Report(One, ReportKind.Step, Send("first held"));

        var outdated = _time.Created[^1];

        throttle.Report(One, ReportKind.Urgent, Send("urgent"));
        throttle.Report(One, ReportKind.Step, Send("second held"));

        outdated.FireRegardless();

        Assert.Equal(["a", "urgent"], _sent);

        _time.Advance(Interval);

        Assert.Equal(["a", "urgent", "second held"], _sent);
    }

    /// <summary>
    /// A clock that stands still until told to move, with timers that fire when it passes their time.
    /// The one in the campaign tests has no timers, and a held step is nothing but a timer.
    /// </summary>
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        internal List<ManualTimer> Created { get; } = [];

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, _ticks + dueTime.Ticks);

            Created.Add(timer);

            return timer;
        }

        internal void Advance(TimeSpan by)
        {
            _ticks += by.Ticks;

            var due = Created.Where(timer => timer.DueAt <= _ticks).OrderBy(timer => timer.DueAt).ToList();

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, long dueAt) : ITimer
    {
        internal long? DueAt { get; private set; } = dueAt;

        internal void Fire()
        {
            DueAt = null;
            callback(state);
        }

        /// <summary>What a callback already on its way does after the timer was disposed.</summary>
        internal void FireRegardless() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose() => DueAt = null;

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
