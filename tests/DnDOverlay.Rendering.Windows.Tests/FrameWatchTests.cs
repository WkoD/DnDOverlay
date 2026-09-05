using DnDOverlay.Rendering.Windows;

namespace DnDOverlay.Rendering.Windows.Tests;

/// <summary>
/// The frame counter, driven by hand.
/// <para>
/// <b>That it can be driven by hand is the whole reason these tests exist.</b> While the counter
/// held the composition hook itself there was nothing to call: a test would have needed a running
/// WPF render loop, so the arithmetic around the tick - the startup exemption, the warning brake,
/// what counts as a frame at all - was carried by the hand-run and by nothing else. The control
/// needed a counter that does not hold the hook, and the same seam that saves a battery lets a test
/// feed it a run in microseconds.
/// </para>
/// </summary>
public sealed class FrameWatchTests
{
    /// <summary>Sixty hertz, as every machine in this project draws at.</summary>
    private const double Cadence = 16.7;

    /// <summary>A pause long enough to be "it stopped" rather than "it was slow" (over 100 ms).</summary>
    private const double Stall = 200;

    private static FrameWatch Watching(List<FrameWindow> windows, List<string> warnings) =>
        FrameWatch.WhileDrawing(
            () => ["stage"],
            windows.Add,
            (surface, _) => warnings.Add(surface));

    private static void Tick(FrameWatch watch, double atMs) =>
        watch.Ticked(TimeSpan.FromMilliseconds(atMs));

    /// <summary>
    /// One reported window: plain frames, with a single long gap in the middle when asked for, and
    /// the gap either announced as a pause or not.
    /// <para>
    /// The gap sits in the MIDDLE rather than at either edge on purpose. A sample lands in the
    /// window it is read in, and a report fires on the first tick PAST the thirty seconds rather
    /// than on the second itself - so a sample at the edge is kept or dropped by a millisecond of
    /// rounding, and a test hung on that would be testing the boundary instead of the rule.
    /// </para>
    /// </summary>
    private static double AWindow(FrameWatch watch, double from, double gapMs = 0, bool announced = false)
    {
        var at = from;

        while (at < from + 15_000)
        {
            at += Cadence;
            Tick(watch, at);
        }

        if (gapMs > 0)
        {
            if (announced)
            {
                watch.Rested();
            }

            at += gapMs;
            Tick(watch, at);
        }

        while (at < from + 30_000 + Cadence)
        {
            at += Cadence;
            Tick(watch, at);
        }

        return at;
    }

    /// <summary>
    /// <b>A stretch in which nothing was drawn is not a frame.</b> The control lets the render hook
    /// go when its stage has nothing waiting, so the next tick may be ten seconds later - and ten
    /// seconds is true about the clock and a lie about the drawing.
    /// </summary>
    [Fact]
    public void An_announced_pause_is_not_counted_as_one_enormous_frame()
    {
        var windows = new List<FrameWindow>();
        var warnings = new List<string>();
        using var watch = Watching(windows, warnings);

        AWindow(watch, from: 0, gapMs: 10_000, announced: true);

        var window = Assert.Single(windows);

        Assert.InRange(window.MaxMs, Cadence - 1, Cadence + 1);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// The counter-check, and it is the one that matters: <b>the same gap, unannounced, is kept</b>
    /// - because then it is a stall, and a stall is exactly what a hand-run is looking for. Without
    /// this the rule above would also pass on a counter that silently threw away every large sample,
    /// which would hide the only finding worth having (Guide <c>C16</c>).
    /// <para>
    /// The two differ in one call and in nothing else. That is deliberate: it is the call that
    /// carries the whole distinction between "the stage was idle" and "the stage was stuck", and
    /// neither the clock nor the size of the gap can tell those apart.
    /// </para>
    /// </summary>
    [Fact]
    public void The_same_gap_unannounced_is_kept_because_then_it_is_a_stall()
    {
        var windows = new List<FrameWindow>();
        var warnings = new List<string>();
        using var watch = Watching(windows, warnings);

        AWindow(watch, from: 0, gapMs: 10_000);

        var window = Assert.Single(windows);

        Assert.InRange(window.MaxMs, 9_000, 11_000);
    }

    /// <summary>
    /// <b>Starting up is allowed to stop once</b>, and only once. The first window is the one in
    /// which the application is still coming up; a warning that fires while everything else is fine
    /// teaches the reader to skip the line.
    /// </summary>
    [Fact]
    public void The_first_window_may_stall_without_warning_and_the_second_may_not()
    {
        var windows = new List<FrameWindow>();
        var warnings = new List<string>();
        using var watch = Watching(windows, warnings);

        var at = AWindow(watch, from: 0, gapMs: Stall);

        Assert.Single(windows);
        Assert.Empty(warnings);

        AWindow(watch, from: at, gapMs: Stall);

        Assert.Equal(2, windows.Count);
        Assert.Equal(["stage"], warnings);
    }

    /// <summary>
    /// <b>The brake, and which way it leans.</b> It hangs on the MEDIAN, so a machine that holds its
    /// cadence warns once and then stays quiet however badly it stalls afterwards. That is what the
    /// M4 hand-run's log looks like - one warning on a maximum of 233.9 ms, then silence through
    /// 898.3 ms two hours later - and it is written down here so the next reader takes the warning
    /// for a doorbell and the reading for the measurement.
    /// </summary>
    [Fact]
    public void A_stall_that_repeats_at_the_same_cadence_warns_only_once()
    {
        var windows = new List<FrameWindow>();
        var warnings = new List<string>();
        using var watch = Watching(windows, warnings);

        var at = AWindow(watch, from: 0, gapMs: Stall);

        at = AWindow(watch, from: at, gapMs: Stall);
        at = AWindow(watch, from: at, gapMs: Stall * 4);

        AWindow(watch, from: at, gapMs: Stall * 4);

        Assert.Equal(4, windows.Count);
        Assert.Equal(["stage"], warnings);

        // The readings say what the warning stopped saying: every one of them carries its maximum.
        Assert.All(windows, window => Assert.True(window.MaxMs > 100));
    }

    /// <summary>
    /// The numbers are rounded once, here, and handed out ready to be written down - so the display
    /// and the control cannot round the same reading two different ways.
    /// </summary>
    [Fact]
    public void A_reading_comes_out_rounded_to_one_decimal()
    {
        var windows = new List<FrameWindow>();
        var warnings = new List<string>();
        using var watch = Watching(windows, warnings);

        AWindow(watch, from: 0);

        var window = Assert.Single(windows);

        Assert.Equal(30, window.Seconds);
        Assert.Equal(Math.Round(window.MedianMs, 1), window.MedianMs);
        Assert.Equal(Math.Round(window.MaxMs, 1), window.MaxMs);
        Assert.Equal(Math.Round(window.BudgetMs, 1), window.BudgetMs);
        Assert.Equal(string.Empty, window.Missing);
    }
}
