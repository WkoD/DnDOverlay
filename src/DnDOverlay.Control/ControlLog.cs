using DnDOverlay.Core;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The operations range, 4000–4999: what the process does to itself - where it stores things,
/// which port it took, that it is going away. It is the fourth range beside connection (1000),
/// assets (2000) and display (3000), and it exists because a data root is none of those three.
/// Numbers are global, strictly ascending within their range and never reused (Part 8).
/// </summary>
internal static partial class ControlLog
{
    /// <summary>
    /// Said out loud on purpose: a development run and an installed copy differ in exactly this
    /// one path, and a run that quietly used the wrong root would be indistinguishable from a
    /// correct one until it had already touched the DM's own campaigns (Part 9).
    /// </summary>
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Data root: {Path}")]
    internal static partial void DataRootChosen(ILogger logger, string path);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Information,
        Message = "No control.json yet - created one, control {ControlId}.")]
    internal static partial void ConfigurationCreated(ILogger logger, Guid controlId);

    /// <summary>
    /// The bad half of a replacement, and it is the one that costs a walk through the flat: with
    /// the identity gone, this control's own displays treat it as a stranger and never knock at
    /// all. "Allowed again" alone would read like "they will be back in a minute", and they will
    /// not (Part 4, Part 6).
    /// <para>
    /// <b>It names the walk and not the call for orphaned devices</b>, although the plan has both:
    /// that grip is M5a and does not exist. A line read in the worst position this program has -
    /// identity gone, every table silent - must not send the reader looking for a function they
    /// cannot find. <b>When M5a builds it, this line and the catalogue entry change together.</b>
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Warning,
        Message = "control.json was unreadable. Set aside as {SetAside}; starting with defaults "
                  + "and a new identity, so paired displays will not find this control by "
                  + "themselves - their pairing has to be reset at each device.")]
    internal static partial void ConfigurationReplaced(ILogger logger, string setAside);

    /// <summary>
    /// The good half, and it deserves its own line rather than silence: an identity that came back
    /// is the difference between one grip here and a walk to every display PC. Said out loud so
    /// that a run which kept it can be told from one that did not - the two look identical at the
    /// moment they happen and quite different ten minutes later.
    /// </summary>
    [LoggerMessage(
        EventId = 4010,
        Level = LogLevel.Warning,
        Message = "control.json was unreadable. Set aside as {SetAside}; the identity {ControlId} "
                  + "was recovered from it, so paired displays find this control again - they "
                  + "arrive as pairing requests, because their tokens went with the file.")]
    internal static partial void IdentityRecovered(ILogger logger, string setAside, Guid controlId);

    /// <summary>
    /// How much of the pairing survived the start. A dropped token means the profile changed -
    /// restored backup, reinstalled Windows, copied installation - and those devices simply pair
    /// again; the line exists so that this is read once instead of guessed at the table
    /// (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 4007,
        Level = LogLevel.Information,
        Message = "{Restored} paired device(s) restored, {Dropped} dropped because the token did not decrypt.")]
    internal static partial void KnownDevicesRestored(ILogger logger, int restored, int dropped);

    /// <summary>
    /// The one startup fault that stops everything: without its hub this application has nothing
    /// to do. It carries the number AND the file to change it in, because a line that only says
    /// "in use" leaves the reader exactly where they were (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 4009,
        Level = LogLevel.Error,
        Message = "Port {Port} is already in use - another control is probably running. Change "
                  + "\"Port\" in {Path} to start a second one.")]
    internal static partial void PortTaken(ILogger logger, Exception exception, int port, string path);

    /// <summary>
    /// The last line before the process goes. It does not catch anything - the fault still ends the
    /// run - it only makes sure the run said what happened.
    /// <para>
    /// Written because of what it cost to be without it: the control died with exit code -1 and the
    /// file ended mid-sentence, so a hand run that had found a real fault could say nothing about
    /// it beyond "it was gone". A crash nobody can read is a crash nobody can fix (Part 1).
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 4011,
        Level = LogLevel.Critical,
        Message = "Unhandled fault on {Where} - this control is going down.")]
    internal static partial void UnhandledFault(ILogger logger, Exception exception, string where);

    /// <summary>
    /// The first line in the asset range, and it exists because its absence was a dead end. Twelve
    /// pictures were taken in at a hand run of M2b and the control's log held <b>not one word</b>
    /// about any of them - so "it takes a few seconds and I do not know why" could not be answered
    /// from the trail at all, only by measuring afterwards on a second machine.
    /// <para>
    /// It carries the duration, because that is the whole question. Measured with the real files:
    /// a 24 MB PNG at 4616×6000 costs <b>11.6 s to normalise</b> and 1.1 s for the thumbnail, while
    /// a 2 MB JPEG costs 1 ms - the JPEG path hands the bytes through and the PNG path decodes and
    /// re-encodes. Without the number in the line, the two look the same from the outside.
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Took {Name} in as {AssetId} ({PixelWidth}x{PixelHeight}, {Bytes} bytes) in {Milliseconds} ms.")]
    internal static partial void AssetTakenIn(
        ILogger logger,
        string name,
        string assetId,
        int pixelWidth,
        int pixelHeight,
        long bytes,
        long milliseconds);

    /// <summary>
    /// A refusal is not a fault of the process and not a silence either - the DM is told at the
    /// panel, and the trail says the same thing, so a picture that "did not work" can be looked up
    /// afterwards (Part 5, Part 8).
    /// </summary>
    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "{Name} was not taken in: {Reason} - {Detail}")]
    internal static partial void AssetRefused(
        ILogger logger, string name, IntakeRejection reason, string detail);

    /// <summary>
    /// The line that answers what 2001 cannot. Two hundred pictures write two hundred 2001 lines,
    /// and reading them tells you about each picture and nothing about the RUN - how long it took
    /// altogether, how many were already there, whether it was broken off halfway.
    /// <para>
    /// Written for a run of one as well, because a single paste is the same path: a line that only
    /// appears above some threshold is a line nobody can rely on finding.
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Intake over {Sources} source(s) finished in {Milliseconds} ms: {Taken} taken in, "
                  + "{Known} already there, {Refused} refused{Broken}.")]
    internal static partial void IntakeFinished(
        ILogger logger, int sources, long milliseconds, int taken, int known, int refused, string broken);

    /// <summary>
    /// The frame times of the last stretch the stage spent drawing.
    /// <para>
    /// <b>The control had no such line at all until the hand-run of M4 asked for one</b>, and the
    /// number could not be handed over because nothing was counting - the frame counter had been
    /// built with the display and lived in it. So the missing number was not unwritten, it was
    /// unmeasured, which is a different and worse thing.
    /// </para>
    /// <para>
    /// It is 4000 and not 3000, although it is the same measurement: the range is chosen by the
    /// SUBJECT of the sentence (Part 8). 3023 is about a screen at the table - what is on it and
    /// whether it holds - while this one is about the process drawing its own window, which is the
    /// same subject as a data root and a taken port.
    /// </para>
    /// <para>
    /// <b>The window covers only the stretches the stage actually drew in</b> (see <c>Redraw</c>).
    /// An idle window has no frame time, it has no frames - so a quiet evening writes no line here
    /// rather than a line full of nothing, and the seconds in the line are the length of the window
    /// and not of the drawing. The FRAME COUNT is in the line for that reason: it says how dense
    /// the window was, and a sparse one is read more carefully.
    /// </para>
    /// <para>
    /// <b>There is no companion warning, and that is a decision rather than an omission.</b> The
    /// budget is derived from the cadence, the cadence is estimated as the 5th percentile of the
    /// intervals, and that estimate needs a dense vsync-paced stream. The second hand-run of M4
    /// measured what happens without one: the stage held a median of 16.7 ms all evening and was
    /// warned for missing a budget of 2.8 ms. Every such warning was false, so the reading stays
    /// and the judgement goes - see <c>FrameWatch.WhileDrawing</c>.
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 4013,
        Level = LogLevel.Information,
        Message = "Stage frames over {Seconds} s, {Frames} of them: median {MedianMs} ms, "
                  + "95th {P95Ms} ms, max {MaxMs} ms, cadence {CadenceMs} ms, CPU {CpuPercent} %, "
                  + "GC {GcMs} ms in {Sweeps} sweep(s), longest draw {DrawMs} ms, "
                  + "hand {HandMs} ms late.")]
    internal static partial void FrameTimes(
        ILogger logger,
        int seconds,
        int frames,
        double medianMs,
        double p95Ms,
        double maxMs,
        double cadenceMs,
        double cpuPercent,
        double gcMs,
        int sweeps,
        double drawMs,
        double handMs);

    /// <summary>
    /// The window fell behind its own event stream and the hub ended it, as it ends any stream
    /// whose reader cannot be served a state event (<c>SessionEvents</c>). This is the line saying
    /// the stream was taken up again.
    /// <para>
    /// <b>It exists because its absence made the fault unfindable.</b> An intake of 723 files put
    /// 714 patches into a queue of 256, the stream was cut, and this window went deaf without a
    /// word - no patch, so no redraw, so no render hook, so not even a frame line for the seven
    /// minutes that followed. Two logs and a code reading were spent on a failure whose only
    /// visible trace was silence. Ending a stream is ordinary hub operation and stays unlogged
    /// there; coming back from one is news about THIS side, and belongs here.
    /// </para>
    /// <para>
    /// <b>Warning rather than Information</b>, and the count is the reason: the events between the
    /// cut and the fresh opening picture are gone. What they carried is put right by the redraw
    /// that follows, so nothing is left wrong - but a number that climbs through an evening says
    /// this window cannot keep up with what the table sends it, which is worth reading.
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 4015,
        Level = LogLevel.Warning,
        Message = "Fell behind the session stream and the hub ended it - taken up again "
                  + "and redrawn from the hub. {Restarts} so far this run.")]
    internal static partial void SessionStreamRestarted(ILogger logger, int restarts);
}
