namespace DnDOverlay.Core;

/// <summary>
/// What a stretch of frames came to: the three numbers the decision in M0 was made on, plus the
/// cadence they are measured against.
/// </summary>
/// <param name="CadenceMs">
/// The fastest the machine actually manages, taken as the 5th percentile of the intervals. It is
/// MEASURED rather than configured, and that is what makes the budget right on a 60 Hz table and on
/// a 120 Hz one without anybody being asked: a composition tick happens at the display's own
/// refresh, so the quickest ticks in a window ARE the hardware's cadence. Reading it from the
/// driver would be a second source that can disagree with the frames actually drawn.
/// </param>
public readonly record struct FrameReading(
    double MedianMs,
    double P95Ms,
    double MaxMs,
    double CadenceMs,
    int Frames)
{
    /// <summary>
    /// What the median has to stay under: the screen's frame interval plus a millisecond (M0,
    /// step 7).
    /// <para>
    /// The extra millisecond is a correction the spike forced: the original threshold read
    /// "median ≤ 16 ms", which is BELOW the vsync interval and therefore unreachable by
    /// construction - a median of 16.7 ms means the full refresh rate is being held, and the
    /// wording would have failed it.
    /// </para>
    /// </summary>
    public double BudgetMs => CadenceMs + 1;

    /// <summary>
    /// Whether this stretch missed the budget. Three questions rather than one, because they fail
    /// differently: the median says the table is generally too slow, the 95th percentile says it
    /// stutters, and the maximum says it stopped.
    /// </summary>
    public bool Missed => MedianMs > BudgetMs || P95Ms > 33 || MaxMs > 100;
}

/// <summary>
/// The frame times of the last stretch, and the one source all three later displays read from.
/// <para>
/// <b>It exists because of the order things are built in.</b> M0 measured the render path and
/// decided the limits; the numbers are needed again at the end of M3, M4 and M5 and on every new
/// display PC - but the diagnostic bar is M5a and the options window M6, both behind the
/// milestones whose acceptance asks for the measurement. So the SOURCE is built here, with the
/// gestures, and until M5a it is read out of the display's own log file (Part 10).
/// </para>
/// <para>
/// <b>One source, three renderings</b> (M5a). Were the statistic to live in the application, the
/// bar, the options window and the diagnostic view would each work it out again - and then there
/// would be three sources with three roundings, which is exactly the thing that makes a number
/// nobody can argue with into three numbers nobody trusts.
/// </para>
/// <para>
/// The clock is handed in as a timestamp per sample (rule 10). This holds no clock of its own, so
/// the same series can be replayed in a test in microseconds.
/// </para>
/// </summary>
public sealed class FrameTimes
{
    /// <summary>
    /// Thirty seconds, as the decision point in M0 used. Long enough that a single hitch does not
    /// move the median, short enough to still be about what is happening right now.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(30);

    private readonly Queue<(long AtMs, double FrameMs)> _frames = new();
    private readonly long _windowMs;

    public FrameTimes(TimeSpan? window = null) =>
        _windowMs = (long)(window ?? DefaultWindow).TotalMilliseconds;

    /// <summary>How many samples are being held - the window's worth, and nothing older.</summary>
    public int Count => _frames.Count;

    /// <summary>
    /// Notes one frame. A sample of zero or less is dropped rather than counted: a composition tick
    /// that reports no time at all is the first tick of a run, where there is nothing to measure
    /// against yet.
    /// </summary>
    public void Add(long atMs, double frameMs)
    {
        if (frameMs <= 0)
        {
            return;
        }

        _frames.Enqueue((atMs, frameMs));

        Forget(atMs);
    }

    /// <summary>
    /// The reading over the window ending now, or <see langword="null"/> while there is not enough
    /// to say anything.
    /// <para>
    /// A threshold of samples rather than of time, and the reason is the failure case: a table that
    /// renders four frames in thirty seconds has certainly missed its budget, but the percentiles of
    /// four samples are not a measurement. Ten is where the numbers start meaning something.
    /// </para>
    /// </summary>
    public FrameReading? Read(long atMs)
    {
        Forget(atMs);

        if (_frames.Count < 10)
        {
            return null;
        }

        var sorted = _frames.Select(frame => frame.FrameMs).Order().ToList();

        return new FrameReading(
            MedianMs: At(sorted, 0.5),
            P95Ms: At(sorted, 0.95),
            MaxMs: sorted[^1],
            CadenceMs: At(sorted, 0.05),
            Frames: sorted.Count);
    }

    /// <summary>Drops everything older than the window - the queue is in time order by construction.</summary>
    private void Forget(long atMs)
    {
        while (_frames.Count > 0 && atMs - _frames.Peek().AtMs > _windowMs)
        {
            _frames.Dequeue();
        }
    }

    /// <summary>
    /// The value at a share of the sorted samples, by nearest rank. No interpolation: these numbers
    /// are read by a person against a threshold, and a 95th percentile that lies between two
    /// measured frames is a number no frame ever took.
    /// </summary>
    private static double At(List<double> sorted, double share) =>
        sorted[Math.Clamp((int)Math.Round((sorted.Count - 1) * share), 0, sorted.Count - 1)];
}
