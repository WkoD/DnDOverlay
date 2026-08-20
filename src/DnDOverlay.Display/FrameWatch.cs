using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using DnDOverlay.Core;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Display;

/// <summary>
/// Counts the frames this process actually draws and says what they came to.
/// <para>
/// <b>The source, and until M5a also the only way to read it</b> (Part 10): the numbers are needed
/// at the end of M3, M4 and M5 and on every new display PC, while the bar at the device is M5a and
/// the options window M6. So the counter is built with the gestures and read out of the log file.
/// The arithmetic is in <see cref="FrameTimes"/>, in Core - here is the tick, the CPU share and the
/// two lines.
/// </para>
/// <para>
/// <b>One counter per process, not per screen, and that is a property of WPF rather than a
/// shortcut:</b> all windows are composed on one render thread, so there is one tick stream. A
/// counter per screen would be three copies of the same number. What IS per screen is the
/// judgement - each screen's budget follows the cadence it is drawn at, and the warning names the
/// screen whose budget was missed (Part 6).
/// </para>
/// </summary>
internal sealed class FrameWatch : IDisposable
{
    /// <summary>
    /// How often the reading goes into the log. The window is thirty seconds, so this is one line
    /// per window - a hand-run reads them in sequence, and a faster line would fill the file with
    /// numbers that overlap each other.
    /// </summary>
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(30);

    /// <summary>How often the UI thread is asked for a moment of its time.</summary>
    private static readonly TimeSpan PulseEvery = TimeSpan.FromMilliseconds(100);

    private readonly FrameTimes _frames = new();
    private readonly ILogger _logger;
    private readonly Func<IReadOnlyCollection<string>> _screens;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// A tick that wants the UI thread every tenth of a second, <b>at the priority the finger's own
    /// events sit at</b>. How late it actually comes is how long a touch would have waited in the
    /// same queue - and that is the difference between "the table stopped drawing" and "the table
    /// stopped listening", which the frame times alone cannot tell apart.
    /// </summary>
    private readonly DispatcherTimer _pulse;

    private long _pulseDueMs;
    private double _lateMs;
    private double _drawMs;
    private double _handMs;

    /// <summary>Screens already warned in this session - the brake against a line nobody reads.</summary>
    private readonly Dictionary<string, double> _warned = new(StringComparer.Ordinal);

    private TimeSpan _lastRender;
    private long _lastReportMs;
    private TimeSpan _lastCpu;
    private long _lastCpuAtMs;
    private double _cpuPercent;

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

    /// <param name="screens">
    /// The screens being played right now, by name. Asked at reporting time rather than kept:
    /// a screen can come and go between two windows, and a list held here would name one that left.
    /// </param>
    internal FrameWatch(ILogger logger, Func<IReadOnlyCollection<string>> screens)
    {
        _logger = logger;
        _screens = screens;
        _lastCpu = Process.GetCurrentProcess().TotalProcessorTime;

        _pulse = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = PulseEvery,
        };

        _pulse.Tick += OnPulse;
        _pulseDueMs = _clock.ElapsedMilliseconds + (long)PulseEvery.TotalMilliseconds;
        _pulse.Start();

        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>
    /// How long the longest drawing of a screen took in this window, in milliseconds. Handed in
    /// from where the drawing happens rather than measured here: only the caller knows where one
    /// drawing begins and ends.
    /// </summary>
    internal void Drew(double milliseconds) => _drawMs = Math.Max(_drawMs, milliseconds);

    /// <summary>
    /// How late one movement of a real hand was handled. The stand-in above says what the queue
    /// costs in general; this says what the finger actually waited for, and the two can differ.
    /// </summary>
    internal void HandWaited(int milliseconds) => _handMs = Math.Max(_handMs, milliseconds);

    private void OnPulse(object? sender, EventArgs e)
    {
        var now = _clock.ElapsedMilliseconds;

        _lateMs = Math.Max(_lateMs, now - _pulseDueMs);
        _pulseDueMs = now + (long)PulseEvery.TotalMilliseconds;
    }

    public void Dispose()
    {
        _pulse.Stop();
        _pulse.Tick -= OnPulse;

        CompositionTarget.Rendering -= OnRendering;
    }

    /// <summary>
    /// One composition tick. <see cref="RenderingEventArgs.RenderingTime"/> rather than a clock of
    /// our own: it is the time the frame was composed at, so the interval between two of them is the
    /// frame time and not the time our handler happened to run.
    /// </summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs rendering)
        {
            return;
        }

        var now = rendering.RenderingTime;
        var atMs = (long)now.TotalMilliseconds;

        if (_lastRender != TimeSpan.Zero)
        {
            _frames.Add(atMs, (now - _lastRender).TotalMilliseconds);
        }

        _lastRender = now;

        if (atMs - _lastReportMs < (long)ReportEvery.TotalMilliseconds)
        {
            return;
        }

        _lastReportMs = atMs;

        Report(atMs);
    }

    private void Report(long atMs)
    {
        if (_frames.Read(atMs) is not { } reading)
        {
            return;
        }

        // Rounded into locals rather than inside the call: the analyser is right that arguments to
        // a logger should cost nothing, and these are read twice anyway.
        var cpu = Round(CpuPercent());
        var median = Round(reading.MedianMs);
        var p95 = Round(reading.P95Ms);
        var max = Round(reading.MaxMs);
        var cadence = Round(reading.CadenceMs);
        var seconds = (int)FrameTimes.DefaultWindow.TotalSeconds;

        var pause = GC.GetTotalPauseDuration();
        var gen2 = GC.CollectionCount(2);
        var gcMs = Round((pause - _lastGcPause).TotalMilliseconds);
        var sweeps = gen2 - _lastGen2;

        _lastGcPause = pause;
        _lastGen2 = gen2;

        var draw = Round(_drawMs);
        var late = Round(_lateMs);
        var hand = Round(_handMs);

        _drawMs = 0;
        _lateMs = 0;
        _handMs = 0;

        DisplayLog.FrameTimes(
            _logger, seconds, median, p95, max, cadence, cpu, gcMs, sweeps, draw, late, hand);

        if (!reading.Missed)
        {
            return;
        }

        foreach (var screen in _screens())
        {
            // Once per session and screen, and again only when it has got markedly worse - a fifth
            // worse is the same brake the stock warning uses. Without it the line would be switched
            // off after the third evening.
            if (_warned.TryGetValue(screen, out var before) && reading.MedianMs < before * 1.2)
            {
                continue;
            }

            _warned[screen] = reading.MedianMs;

            var budget = Round(reading.BudgetMs);
            var stutter = Round(reading.StutterMs);

            DisplayLog.FrameBudgetMissed(
                _logger, screen, reading.Missing, median, budget, p95, stutter, max, cpu);
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
