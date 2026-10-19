using System.Globalization;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The one path every entrance ends on (Part 7): take in, then - when a screen was the target - show.
/// One picture or two hundred, the same way, with progress, break-off and ONE collected message.
/// <para>
/// <b>Every picture lands in the stock, whatever it was aimed at</b> - a drop onto the panel, a
/// paste, a drop from Explorer onto a tile. Otherwise there would be pictures in the stock the DM
/// cannot find. A drop onto the panel stops there; a drop onto a tile then puts it on that screen,
/// at the place it was let go (Part 7: ingest, entry, only then <c>AddItem</c>).
/// </para>
/// <para>
/// Moved out of the M2 scaffold when the campaign panel took its place (M5b); the order of the
/// run, the logging and the message are as they were.
/// </para>
/// </summary>
internal sealed class IntakeRunner(ISessionApi session, Entrances entrances, Action<string> say, ILogger logger)
{
    private CancellationTokenSource? _running;

    /// <summary>A run started or ended - the grips that start one go dead meanwhile, the break-off comes alive.</summary>
    internal event EventHandler<bool>? RunningChanged;

    /// <summary>Whether a run is going.</summary>
    internal bool Running => _running is not null;

    /// <summary>The entrances, for the grips that read a clipboard, a drop or a file choice.</summary>
    internal Entrances Entrances => entrances;

    /// <summary>Breaks the run off. What came in stays - every finished picture is a valid entry.</summary>
    internal void BreakOff() => _running?.Cancel();

    /// <summary>
    /// Takes these in and, with a target, shows them.
    /// </summary>
    /// <param name="target">The screen to put them on, or <see langword="null"/> for the stock only.</param>
    /// <param name="at">Where on that screen it was let go - the first picture lands there, several flow.</param>
    /// <param name="background">The first picture becomes the target's background instead.</param>
    internal async Task RunAsync(IReadOnlyList<IntakeSource> sources, ScreenRef? target, Point? at = null, bool background = false)
    {
        if (sources.Count == 0)
        {
            say("Nothing in that could be taken in.");
            return;
        }

        if (_running is not null)
        {
            say("A run is going. Break it off first, or wait for it.");
            return;
        }

        using var running = new CancellationTokenSource();

        _running = running;
        RunningChanged?.Invoke(this, true);

        var clock = System.Diagnostics.Stopwatch.StartNew();

        IntakeReport report;

        try
        {
            var progress = new Progress<IntakeProgress>(step => say(Progressing(step)));

            // OFF the UI thread: the stock's ingest hashes, decodes, normalises, writes and
            // thumbnails, and awaited here it froze the control for a 20 MB picture (hand-run of
            // M2b, step 17).
            report = await Task.Run(() => entrances.TakeInAsync(sources, progress, running.Token)).ConfigureAwait(true);
        }
        finally
        {
            _running = null;
            RunningChanged?.Invoke(this, false);
        }

        ControlLog.IntakeFinished(
            logger,
            sources.Count,
            clock.ElapsedMilliseconds,
            report.Taken.Count,
            report.AlreadyPresent.Count,
            report.Refused.Count,
            report.Cancelled ? ", broken off" : string.Empty);

        foreach (var taken in report.Taken)
        {
            ControlLog.AssetTakenIn(
                logger,
                taken.Asset.Name,
                taken.Asset.AssetId.Value,
                taken.Asset.Meta.PixelWidth,
                taken.Asset.Meta.PixelHeight,
                taken.Asset.Meta.Bytes,
                taken.Milliseconds);
        }

        foreach (var failure in report.Refused)
        {
            ControlLog.AssetRefused(logger, failure.Name, failure.Reason, failure.Detail);
        }

        var unused = target is { } screen ? await ShowAsync(screen, report, at, background).ConfigureAwait(true) : null;

        say(unused is null ? Collected(report) : Collected(report) + " " + unused);
    }

    /// <summary>
    /// Puts what came in onto the screen, IN THE ORDER THE DM CHOSE IT - choosing first means lying
    /// underneath (hand-run of M3). The background layer holds one picture: a run of several meant as
    /// a background uses its first and says what it did not use.
    /// </summary>
    private async Task<string?> ShowAsync(ScreenRef screen, IntakeReport report, Point? at, bool background)
    {
        var placeable = report.Placeable;

        if (placeable.Count == 0)
        {
            return null;
        }

        if (background)
        {
            await session.SetBackgroundAsync(screen, placeable[0]).ConfigureAwait(true);

            return placeable.Count == 1
                ? null
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{placeable[0].Name} is the background; the other {placeable.Count - 1} went into the stock only.");
        }

        // One picture let go at a place lands there; a run flows from the placement of the screen -
        // ONE command, one patch, as since 07.09.2026.
        if (placeable.Count == 1 && at is { } place)
        {
            await session.AddItemAsync(screen, placeable[0], place).ConfigureAwait(true);
        }
        else
        {
            await session.AddItemsAsync(screen, placeable).ConfigureAwait(true);
        }

        return null;
    }

    private static string Progressing(IntakeProgress step) =>
        step.Total == 1
            ? $"Taking {step.Name} in - this can take a few seconds for a large picture ..."
            : string.Create(CultureInfo.InvariantCulture, $"{step.Done} of {step.Total} ... {step.Name}");

    /// <summary>ONE collected message, not one dialogue per file (Part 7).</summary>
    private static string Collected(IntakeReport report)
    {
        var parts = new List<string>();

        if (report.Taken.Count > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{report.Taken.Count} taken in"));
        }

        if (report.AlreadyPresent.Count > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{report.AlreadyPresent.Count} already in the stock"));
        }

        if (report.Refused.Count > 0)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{report.Refused.Count} refused ({string.Join("; ", report.Refused.Select(f => $"{f.Name}: {f.Detail}"))})"));
        }

        if (report.Tolerated.Count > 0)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{report.Tolerated.Count} in a format that is not assured ({string.Join(", ", report.Tolerated)})"));
        }

        return parts.Count == 0
            ? "Nothing came in."
            : string.Join(", ", parts) + (report.Cancelled ? " - broken off, what came in stays." : ".");
    }
}
