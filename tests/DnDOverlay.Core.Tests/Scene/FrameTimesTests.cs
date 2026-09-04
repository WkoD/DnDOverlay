using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The frame-time source. It gets a test although no step in Part 11 asks for one: a wrongly
/// computed figure would first be noticed at the table, where it looks like a slow machine rather
/// than like arithmetic (<c>checks/M3.md</c>).
/// </summary>
public sealed class FrameTimesTests
{
    /// <summary>A steady series at the given interval, one sample per interval.</summary>
    private static FrameTimes Steady(double frameMs, int count, double windowSeconds = 30)
    {
        var times = new FrameTimes(TimeSpan.FromSeconds(windowSeconds));

        for (var frame = 0; frame < count; frame++)
        {
            times.Add((long)(frame * frameMs), frameMs);
        }

        return times;
    }

    [Fact]
    public void Too_few_frames_say_nothing_at_all()
    {
        Assert.Null(Steady(16.7, 9).Read(1000));
    }

    [Fact]
    public void A_steady_series_reads_as_that_interval()
    {
        var reading = Steady(16.7, 600).Read(10_000);

        Assert.NotNull(reading);
        Assert.Equal(16.7, reading.Value.MedianMs, precision: 6);
        Assert.Equal(16.7, reading.Value.MaxMs, precision: 6);
        Assert.Equal(16.7, reading.Value.CadenceMs, precision: 6);
    }

    /// <summary>
    /// <b>The budget is measured, not configured</b> - and this is the test that says why it matters:
    /// the same code has to be right on a 60 Hz table and on a 120 Hz one, and nobody is asked.
    /// </summary>
    [Theory]
    [InlineData(16.7)]
    [InlineData(8.3)]
    [InlineData(6.9)]
    public void The_budget_follows_the_cadence_the_machine_actually_manages(double frameMs)
    {
        var reading = Steady(frameMs, 300).Read(10_000);

        Assert.NotNull(reading);
        Assert.Equal(frameMs + 1, reading.Value.BudgetMs, precision: 6);
        Assert.False(reading.Value.Missed);
    }

    /// <summary>
    /// A table that generally cannot keep up: the median sits above the cadence it manages at its
    /// best, which is exactly the shape of "the table is sluggish" (M0, step 7).
    /// </summary>
    [Fact]
    public void A_median_above_the_cadence_misses_the_budget()
    {
        var times = new FrameTimes();

        // A tenth of the frames at the machine's own cadence, the rest at twice it.
        for (var frame = 0; frame < 200; frame++)
        {
            times.Add(frame * 20, frame % 10 == 0 ? 16.7 : 33.4);
        }

        var reading = times.Read(4000);

        Assert.NotNull(reading);
        Assert.Equal(16.7, reading.Value.CadenceMs, precision: 6);
        Assert.True(reading.Value.Missed, "a median at twice the cadence passed the budget");
    }

    /// <summary>
    /// <b>One doubled frame in twenty is not a stutter</b>, and this is the case the flat number
    /// got wrong: two frames at 60 Hz are 33.3 ms, the threshold read <c>33</c>, so the reading the
    /// rule meant to allow failed it by a third of a millisecond. Measured on the Pro 4, which
    /// produces exactly this percentile while it is doing nothing at all - the threshold fired at
    /// rest (hand-run of M3b, step 37a).
    /// <para>
    /// Asserted at both refresh rates, because the point of following the cadence is that nobody is
    /// asked which one the screen has.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(16.7)]
    [InlineData(8.3)]
    public void A_doubled_frame_in_twenty_is_not_a_stutter(double frameMs)
    {
        var times = new FrameTimes();

        // A tenth doubled rather than a twentieth: at exactly five percent the percentile lands
        // just under them and the case would never reach the threshold it is about.
        for (var frame = 0; frame < 200; frame++)
        {
            times.Add(frame * 20, frame % 10 == 0 ? frameMs * 2 : frameMs);
        }

        var reading = times.Read(4000);

        Assert.NotNull(reading);
        Assert.Equal(frameMs * 2, reading.Value.P95Ms, precision: 6);
        Assert.False(reading.Value.Missed, "a single doubled frame in twenty counted as a stutter");
    }

    /// <summary>
    /// The warning has to name what gave way. It read <i>"is not holding its frame budget: median
    /// 16.7 ms against 17.7 ms"</i> at the table - accusing the one number that was fine, while the
    /// maximum that actually broke stood further along the same sentence.
    /// </summary>
    [Fact]
    public void The_reading_names_which_of_the_three_gave_way()
    {
        var times = new FrameTimes();

        for (var frame = 0; frame < 200; frame++)
        {
            times.Add(frame * 20, frame == 100 ? 4000 : 16.7);
        }

        var reading = times.Read(4000);

        Assert.NotNull(reading);
        Assert.True(reading.Value.Missed);
        Assert.Equal("max", reading.Value.Missing);
    }

    /// <summary>And all three when all three did.</summary>
    [Fact]
    public void All_three_are_named_when_all_three_gave_way()
    {
        var times = new FrameTimes();

        for (var frame = 0; frame < 200; frame++)
        {
            times.Add(frame * 20, frame % 10 == 0 ? 16.7 : 400);
        }

        var reading = times.Read(4000);

        Assert.NotNull(reading);
        Assert.Equal("median, 95th, max", reading.Value.Missing);
    }

    /// <summary>And the other side of the same line: what the rule is actually about is a percentile ABOVE
    /// two frames. Without this the change above would have widened the threshold to "never fires".
    /// </summary>
    [Fact]
    public void A_percentile_above_two_frames_is_a_stutter()
    {
        var times = new FrameTimes();

        for (var frame = 0; frame < 200; frame++)
        {
            times.Add(frame * 20, frame % 10 == 0 ? 50 : 16.7);
        }

        var reading = times.Read(4000);

        Assert.NotNull(reading);
        Assert.Equal(16.7, reading.Value.MedianMs, precision: 6);
        Assert.True(reading.Value.Missed, "a percentile at three frames passed as steady");
    }

    /// <summary>
    /// Stuttering fails on its own number even when the median is fine - which is the point of
    /// having three: they fail differently and mean different things.
    /// </summary>
    [Fact]
    public void A_series_that_stutters_misses_on_the_percentile_and_the_maximum()
    {
        var times = new FrameTimes();

        for (var frame = 0; frame < 100; frame++)
        {
            times.Add(frame * 17, frame % 20 == 0 ? 120 : 16.7);
        }

        var reading = times.Read(2000);

        Assert.NotNull(reading);
        Assert.Equal(16.7, reading.Value.MedianMs, precision: 6);
        Assert.Equal(120, reading.Value.MaxMs, precision: 6);
        Assert.True(reading.Value.Missed);
    }

    /// <summary>Only the window counts: what happened a minute ago says nothing about now.</summary>
    [Fact]
    public void Samples_older_than_the_window_fall_out()
    {
        var times = new FrameTimes(TimeSpan.FromSeconds(1));

        for (var frame = 0; frame < 50; frame++)
        {
            times.Add(frame * 10, 100);
        }

        for (var frame = 0; frame < 50; frame++)
        {
            times.Add(2000 + (frame * 10), 16.7);
        }

        var reading = times.Read(2500);

        Assert.NotNull(reading);
        Assert.Equal(50, reading.Value.Frames);
        Assert.Equal(16.7, reading.Value.MaxMs, precision: 6);
    }

    /// <summary>
    /// The first tick of a run has nothing to measure against. Counting its zero would put a
    /// cadence of nought under the budget and make every reading a miss.
    /// </summary>
    [Fact]
    public void A_sample_of_no_time_at_all_is_not_a_frame()
    {
        var times = new FrameTimes();

        times.Add(0, 0);
        times.Add(0, -5);

        Assert.Equal(0, times.Count);
    }

    /// <summary>
    /// <b>"Something stopped it once" is a different statement from "it is too slow"</b>, and the
    /// display needs to tell them apart to let the opening stretch off. Measured on the SP7
    /// (hand-run of M3, B1): 653.5 ms maximum in the first window against 49-85 ms in every window
    /// after it, median 16.9 ms throughout - the application coming up, not the table being slow.
    /// </summary>
    [Theory]
    // median, 95th, max, only the maximum gave way?
    [InlineData(16.9, 33.3, 653.5, true)]
    [InlineData(16.9, 33.3, 84.7, false)]   // nothing gave way at all
    [InlineData(40.0, 33.3, 653.5, false)]  // the median went too, so the table IS slow
    [InlineData(16.9, 90.0, 653.5, false)]  // and so did the 95th: it stutters
    public void The_maximum_can_give_way_on_its_own(double median, double p95, double max, bool only)
    {
        var reading = new FrameReading(median, p95, max, CadenceMs: 16.7, Frames: 1800);

        Assert.Equal(only, reading.OnlyStopped);

        // And it never says "only" about a stretch that did not miss at all - the display asks the
        // two together, so a quiet window must answer no to both.
        Assert.True(reading.Missed || !reading.OnlyStopped);
    }
}
