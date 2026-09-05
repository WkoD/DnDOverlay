using System.Diagnostics;
using System.Windows.Media;
using DnDOverlay.Core;

namespace DnDOverlay.Rendering.Windows;

/// <summary>
/// One window's worth of frames, rounded and ready to be written to a log.
/// <para>
/// <b>It carries rounded numbers, and that is the point of it existing at all.</b> The rounding is
/// a decision - one decimal, because the fourth decimal of a frame time makes two equal readings
/// look different - and <see cref="FrameTimes"/> says in its own summary what happens when a
/// decision like that is taken in more than one place: three sources with three roundings turn a
/// number nobody can argue with into three numbers nobody trusts. So it is done once, here, and
/// both applications write down what they are handed.
/// </para>
/// </summary>
/// <param name="Seconds">The length of the window these numbers cover.</param>
/// <param name="Missing">Which of the three thresholds gave way, empty when none did.</param>
public sealed record FrameWindow(
    int Seconds,
    double MedianMs,
    double P95Ms,
    double MaxMs,
    double CadenceMs,
    double CpuPercent,
    double GcMs,
    int Sweeps,
    double DrawMs,
    double HandMs,
    double BudgetMs,
    double StutterMs,
    string Missing);

/// <summary>
/// Counts the frames a process actually draws and says what they came to.
/// <para>
/// <b>The source, and until M5a also the only way to read it</b> (Part 10): the numbers are needed
/// at the end of M3, M4 and M5 and on every new display PC, while the bar at the device is M5a and
/// the options window M6. So the counter is built with the gestures and read out of the log file.
/// The arithmetic is in <see cref="FrameTimes"/>, in Core - here is the tick, the CPU share and the
/// two readings.
/// </para>
/// <para>
/// <b>One counter per process, not per screen, and that is a property of WPF rather than a
/// shortcut:</b> all windows are composed on one render thread, so there is one tick stream. A
/// counter per screen would be three copies of the same number. What IS per screen is the
/// judgement - each screen's budget follows the cadence it is drawn at, and the warning names the
/// surface whose budget was missed (Part 6).
/// </para>
/// <para>
/// <b>It lives here rather than in the display because the control needs it too, and had nothing.</b>
/// The hand-run of M4 asked for the control's frame times and could not be given any: there was no
/// counter in that process at all, so the missing number was not unwritten but unmeasured. Copying
/// this class over would have been the second tick stream this whole design exists against - and
/// the two applications do not even log it under the same identifier, because the number IS the
/// contract (Part 8). Hence no logger in here: what it does is measure and hand out
/// <see cref="FrameWindow"/>, and each application writes its own line.
/// </para>
/// <para>
/// <b>The two ways to drive it</b> are <see cref="Always"/> and <see cref="WhileDrawing"/>, and the
/// difference is not a preference. See <see cref="WhileDrawing"/>.
/// </para>
/// </summary>
public sealed class FrameWatch : IDisposable
{
    /// <summary>
    /// How often the reading goes into the log. The window is thirty seconds, so this is one line
    /// per window - a hand-run reads them in sequence, and a faster line would fill the file with
    /// numbers that overlap each other.
    /// </summary>
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(30);

    private readonly FrameTimes _frames = new();
    private readonly Func<IReadOnlyCollection<string>> _surfaces;
    private readonly Action<FrameWindow> _report;
    private readonly Action<string, FrameWindow> _warn;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Whether this watch holds the render hook itself - see <see cref="Always"/>.</summary>
    private readonly bool _hooked;

    private double _drawMs;
    private double _handMs;

    /// <summary>Surfaces already warned in this session - the brake against a line nobody reads.</summary>
    private readonly Dictionary<string, double> _warned = new(StringComparer.Ordinal);

    private TimeSpan _lastRender;
    private long _lastReportMs;
    private TimeSpan _lastCpu;
    private long _lastCpuAtMs;
    private double _cpuPercent;

    /// <summary>Whether the stretch about to be reported is the first of this run.</summary>
    private bool _first = true;

    /// <summary>
    /// What the garbage collector had stopped the world for when the last window was reported, and
    /// how many times it had swept the oldest generation.
    /// <para>
    /// <b>They are here because the maximum alone cannot say who took the second.</b> Measured at
    /// the table (hand-run of M3b, 37c1): frames of up to 4.1 s while the median stayed at 16.7 -
    /// so the table stops dead a few times a minute and every average says it is fine. A pause of
    /// the collector stops every thread, which looks exactly like that; so does a decode on the
    /// wrong thread. These two numbers separate the two without a guess (Guide <c>G1</c>).
    /// </para>
    /// </summary>
    private TimeSpan _lastGcPause;
    private int _lastGen2;

    /// <param name="surfaces">
    /// The things this reading is judged for, by name - the display names the screens it is
    /// playing, the control names its one stage. Two jobs, and both need the name: it is the key
    /// the once-per-session brake hangs on, and it is what the caller may put in its warning.
    /// Asked at reporting time rather than kept: a screen can come and go between two windows, and
    /// a list held here would name one that left.
    /// </param>
    /// <param name="report">Called once per window, always - a reading is not a complaint.</param>
    /// <param name="warn">
    /// Called per surface whose budget was missed, and only when the brake lets it through.
    /// </param>
    private FrameWatch(
        Func<IReadOnlyCollection<string>> surfaces,
        Action<FrameWindow> report,
        Action<string, FrameWindow> warn,
        bool hooked)
    {
        _surfaces = surfaces;
        _report = report;
        _warn = warn;
        _hooked = hooked;
        _lastCpu = Process.GetCurrentProcess().TotalProcessorTime;

        if (hooked)
        {
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>
    /// A watch that holds the render hook itself and therefore measures every frame of the run -
    /// what the display wants, because it is showing a table all evening whether or not anything on
    /// it moves.
    /// </summary>
    public static FrameWatch Always(
        Func<IReadOnlyCollection<string>> surfaces,
        Action<FrameWindow> report,
        Action<string, FrameWindow> warn) =>
        new(surfaces, report, warn, hooked: true);

    /// <summary>
    /// A watch the caller ticks, for a process that draws in bursts - what the control wants.
    /// <para>
    /// <b>It may not hold the hook, and the reason is written into <c>Redraw</c>:</b>
    /// <c>CompositionTarget.Rendering</c> fires on every frame for as long as anybody listens, so a
    /// permanent subscription would keep the control drawing at sixty frames a second through an
    /// evening in which nothing moves - on a machine running off a battery. Measuring the stage
    /// would then be the thing that cost the stage its battery.
    /// </para>
    /// <para>
    /// <b>What is lost by that is nothing worth having.</b> The control draws exactly while
    /// something is happening, and that is the only stretch in which its frame time means anything:
    /// an idle window that renders nothing has no frame time, it has no frames. The number this
    /// produces answers the question the hand-run actually asks - does the stage keep up while the
    /// DM is moving a picture - rather than an average over an evening of standing still.
    /// </para>
    /// <para>
    /// The caller ticks with <see cref="Ticked"/> and says <see cref="Rested"/> when it lets the
    /// hook go. That second call is what keeps a quiet minute from being counted as one frame of
    /// sixty thousand milliseconds.
    /// </para>
    /// </summary>
    public static FrameWatch WhileDrawing(
        Func<IReadOnlyCollection<string>> surfaces,
        Action<FrameWindow> report,
        Action<string, FrameWindow> warn) =>
        new(surfaces, report, warn, hooked: false);

    /// <summary>
    /// How long the longest drawing of a surface took in this window, in milliseconds. Handed in
    /// from where the drawing happens rather than measured here: only the caller knows where one
    /// drawing begins and ends.
    /// </summary>
    public void Drew(double milliseconds) => _drawMs = Math.Max(_drawMs, milliseconds);

    /// <summary>
    /// How late one movement of a real hand was handled.
    /// <para>
    /// <b>It replaced a stand-in, and the stand-in was wrong.</b> A <c>DispatcherTimer</c> at the
    /// finger's priority was supposed to say what the queue costs; measured on the Pro 4 it reported
    /// 183 to 434 ms while the machine was idle at 1 % CPU, so what it timed was Windows coalescing
    /// its timers, not our queue. The event's own stamp cannot be wrong in that way: it is the
    /// moment the system saw the finger.
    /// </para>
    /// </summary>
    public void HandWaited(int milliseconds) => _handMs = Math.Max(_handMs, milliseconds);

    /// <summary>
    /// One composition tick, for a watch that does not hold the hook. Hand in
    /// <see cref="RenderingEventArgs.RenderingTime"/> rather than a clock of the caller's own: it is
    /// the time the frame was composed at, so the interval between two of them is the frame time and
    /// not the time the handler happened to run.
    /// </summary>
    public void Ticked(TimeSpan renderingTime)
    {
        var atMs = (long)renderingTime.TotalMilliseconds;

        if (_lastRender != TimeSpan.Zero)
        {
            _frames.Add(atMs, (renderingTime - _lastRender).TotalMilliseconds);
        }

        _lastRender = renderingTime;

        if (atMs - _lastReportMs < (long)ReportEvery.TotalMilliseconds)
        {
            return;
        }

        _lastReportMs = atMs;

        Report(atMs);
    }

    /// <summary>
    /// Said when the caller lets the render hook go: <b>the next tick begins a new stretch and the
    /// gap between them is not a frame.</b>
    /// <para>
    /// Without it a stage that stood still for a minute and was then touched would report a single
    /// frame of sixty thousand milliseconds - a maximum that is true about the clock and a lie about
    /// the drawing. The samples already collected are kept: they are what the window is made of, and
    /// <see cref="FrameTimes"/> forgets them by age on its own.
    /// </para>
    /// </summary>
    public void Rested() => _lastRender = TimeSpan.Zero;

    public void Dispose()
    {
        if (_hooked)
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs rendering)
        {
            Ticked(rendering.RenderingTime);
        }
    }

    private void Report(long atMs)
    {
        if (_frames.Read(atMs) is not { } reading)
        {
            return;
        }

        var pause = GC.GetTotalPauseDuration();
        var gen2 = GC.CollectionCount(2);

        var window = new FrameWindow(
            Seconds: (int)FrameTimes.DefaultWindow.TotalSeconds,
            MedianMs: Round(reading.MedianMs),
            P95Ms: Round(reading.P95Ms),
            MaxMs: Round(reading.MaxMs),
            CadenceMs: Round(reading.CadenceMs),
            CpuPercent: Round(CpuPercent()),
            GcMs: Round((pause - _lastGcPause).TotalMilliseconds),
            Sweeps: gen2 - _lastGen2,
            DrawMs: Round(_drawMs),
            HandMs: Round(_handMs),
            BudgetMs: Round(reading.BudgetMs),
            StutterMs: Round(reading.StutterMs),
            Missing: reading.Missing);

        _lastGcPause = pause;
        _lastGen2 = gen2;
        _drawMs = 0;
        _handMs = 0;

        _report(window);

        var starting = _first;

        _first = false;

        // <b>Starting up is allowed to stop once.</b> The first stretch is the one in which the
        // application is still coming up and the first pictures are decoded and hung up, and a
        // single stall there is not the table being slow - measured on the SP7, 653.5 ms in the
        // opening window against 49-85 ms in every window after it (hand-run of M3, B1). The
        // exception is as narrow as it can be: only the MAXIMUM is forgiven, only while the median
        // and the 95th are inside their budgets, and only in that one window. Anything that gets
        // worse later is by definition not in the first window and warns as before.
        //
        // It stays observed rather than hidden: the maximum is in every reading whether or not this
        // one warns, so a startup that grows worse over the months can be read off the log.
        if (!reading.Missed || (starting && reading.OnlyStopped))
        {
            return;
        }

        foreach (var surface in _surfaces())
        {
            // Once per session and surface, and again only when it has got markedly worse - a fifth
            // worse is the same brake the stock warning uses. Without it the line would be switched
            // off after the third evening.
            //
            // Read at the table and worth knowing when reading a log: the brake hangs on the
            // MEDIAN, which on a machine that holds its cadence never moves at all. In the M4
            // hand-run the display warned once, on a maximum of 233.9 ms, and then stayed silent
            // through a maximum of 898.3 ms two hours later. That is this brake working as written -
            // the warning is a doorbell, and the reading is the line above it.
            if (_warned.TryGetValue(surface, out var before) && reading.MedianMs < before * 1.2)
            {
                continue;
            }

            _warned[surface] = reading.MedianMs;

            _warn(surface, window);
        }
    }

    /// <summary>
    /// How much of one core this process has used since the last reading. Across all cores, because
    /// the question is what the machine is spending on us and a percentage per core would read as
    /// "12 %" on a machine that is fully occupied by us.
    /// </summary>
    private double CpuPercent()
    {
        var elapsedMs = _clock.ElapsedMilliseconds - _lastCpuAtMs;

        if (elapsedMs <= 0)
        {
            return _cpuPercent;
        }

        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var used = (cpu - _lastCpu).TotalMilliseconds;

        _lastCpu = cpu;
        _lastCpuAtMs = _clock.ElapsedMilliseconds;
        _cpuPercent = used / elapsedMs / Environment.ProcessorCount * 100;

        return _cpuPercent;
    }

    /// <summary>
    /// One decimal. These numbers are read by a person against a threshold, and the fourth decimal
    /// of a frame time is noise that makes two readings look different when they are not.
    /// </summary>
    private static double Round(double value) => Math.Round(value, 1);
}
