using DnDOverlay.Core;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Display;

/// <summary>
/// The display application's log messages. They do not all sit in one range, and that is the
/// rule rather than an exception: <b>the range follows the subject of the sentence, never the
/// assembly it is written in</b> (Part 8). What is on a screen is display (3000), what this
/// process does to itself is operations (4000), and who is talking to whom - pairing included -
/// is connection (1000), the same range the hub writes into.
/// <para>
/// Numbers are global, strictly ascending within their range and never reused; the catalogue
/// lives in <c>docs/protocol.md</c>.
/// </para>
/// </summary>
internal static partial class DisplayLog
{
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Screen {ScreenId} ({Label}): {Size} at {Dpi} DPI.")]
    internal static partial void ScreenFound(ILogger logger, ScreenId screenId, string label, PixelSize size, double dpi);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "Windows reports no screen to play on.")]
    internal static partial void NoScreens(ILogger logger);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Overlay on {ScreenId} opened ({Mode}).")]
    internal static partial void OverlayOpened(ILogger logger, ScreenId screenId, string mode);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Warning,
        Message = "Discarding an operation for screen {ScreenId}, which this device does not have.")]
    internal static partial void UnknownScreenDiscarded(ILogger logger, ScreenId screenId);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        Message = "Could not load asset {Name} ({AssetId}): {Detail}")]
    internal static partial void AssetFailed(ILogger logger, string name, AssetId assetId, string detail);

    /// <summary>
    /// One picture became a bitmap. <b>The duration is here because the hand-run of M3b could not
    /// tell a slow decode from a stopped thread:</b> between "the ring is full" and "the picture is
    /// there" the table answered nothing, and the line said only how many pixels came out. What it
    /// costs is the first half of that gap; the frame line's GC numbers are the second.
    /// </summary>
    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Information,
        Message = "Asset {Name} ({AssetId}) decoded at {Width}x{Height} in {Milliseconds} ms.")]
    internal static partial void AssetDecoded(
        ILogger logger, string name, AssetId assetId, int width, int height, long milliseconds);

    /// <summary>
    /// What one load run came to, said once at the end rather than per picture.
    /// <para>
    /// The M2c hand-run had to time the pictures with a stopwatch: the only duration in either
    /// process was the control's 2001, and that measures the INGEST - hash, decode, normalise,
    /// store - which reads "0 ms" for a small file and says nothing about the wire. This is the
    /// number the step called "Zeitvorgabe" was actually asking for.
    /// </para>
    /// <para>
    /// The peak is in the same line on purpose. "The pictures came one after another" and "three
    /// came at once but each was slow" look identical from the room, and they need opposite
    /// answers - so the reading that tells them apart belongs where the duration is (Part 5,
    /// Part 8).
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 3020,
        Level = LogLevel.Information,
        Message = "Loaded {Fetched} asset(s) in {Milliseconds} ms, {Bytes} bytes, {Peak} at a time; {AlreadyHere} already here.")]
    internal static partial void AssetsLoaded(
        ILogger logger, int fetched, long milliseconds, long bytes, int peak, int alreadyHere);

    /// <summary>
    /// The surface a scene is drawn on, beside the screen it is meant to cover.
    /// <para>
    /// A picture is stretched exactly when these two disagree in SHAPE: the scene is normalised
    /// against the screen's aspect ratio and then drawn onto this surface. The second hand-run
    /// reported stretching after a change from 1920×1080 to 1600×1200 that nothing in the log could
    /// explain (37c3) - this is the line that will say which of the two was wrong.
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 3027,
        Level = LogLevel.Information,
        Message = "{ScreenId} draws on {WidthDip}x{HeightDip} DIP for a screen of {Size} at {Dpi} DPI.")]
    internal static partial void SurfaceMeasured(
        ILogger logger, ScreenId screenId, int widthDip, int heightDip, PixelSize size, double dpi);

    /// <summary>
    /// What the render path is, written once at startup.
    /// <para>
    /// <b>Part 11 asks for it in the log's HEADER, and it cannot go there:</b> the header is written
    /// by <c>ProcessLog</c> in Core, which knows nothing about WPF and must not (Part 2). So it is
    /// the display's first line instead - same file, same run, one line further down.
    /// </para>
    /// <para>
    /// <c>Tier</c> describes the hardware and not the layered window - tier 2 under forced software
    /// rendering is measured (Part 6), so it is a negative test only: <c>0</c> means software for
    /// certain. The MODE beside it is the thing the counter-test of 37a switches, and without this
    /// line there is no way to tell whether it took - which is exactly how the first hand-run ended,
    /// with "the registry edit had no effect, wrong command?".
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 3026,
        Level = LogLevel.Information,
        Message = "Render path: tier {Tier}, mode {Mode}.")]
    internal static partial void RenderPath(ILogger logger, int tier, string mode);

    /// <summary>
    /// A picture was decoded a step larger because it is being drawn larger than it was decoded.
    /// Debug rather than Information: it happens whenever somebody zooms, and it is a reading for
    /// the evening when the table went soft or slow, not news (Part 6).
    /// </summary>
    [LoggerMessage(
        EventId = 3025,
        Level = LogLevel.Debug,
        Message = "{Name} decoded again at {StepPixels} px, up from {BeforePixels} px.")]
    internal static partial void PictureSharpened(
        ILogger logger, string name, int beforePixels, int stepPixels);

    /// <summary>
    /// The frame times of the last stretch. <b>Until M5a this line IS the display</b> - the
    /// diagnostic bar and the options window are later milestones, and the acceptance of M3, M4 and
    /// M5 each asks for the measurement (Part 10). It goes out on Information because it is a
    /// reading and not a complaint; whether the budget was missed is the line below.
    /// </summary>
    [LoggerMessage(
        EventId = 3023,
        Level = LogLevel.Information,
        Message = "Frames over {Seconds} s: median {MedianMs} ms, 95th {P95Ms} ms, max {MaxMs} ms, "
            + "cadence {CadenceMs} ms, CPU {CpuPercent} %, GC {GcMs} ms in {Sweeps} sweep(s), "
            + "longest draw {DrawMs} ms, hand {HandMs} ms late.")]
    internal static partial void FrameTimes(
        ILogger logger,
        int seconds,
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
    /// The device says of its own accord that a screen is not holding its budget (Part 6).
    /// <para>
    /// <b>Once per session and screen</b>, and again only on a marked deterioration - the same brake
    /// the stock warning has. Without it the line would be switched off after the third evening, and
    /// then it would be missing exactly when it is needed. Its whole purpose is that "the table is
    /// juddering" stops being the DM's diagnosis and becomes a line with numbers in it.
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 3024,
        Level = LogLevel.Warning,
        Message = "{ScreenName} is not holding its frame budget, {Missing} over: median {MedianMs} ms "
            + "against {BudgetMs} ms, 95th {P95Ms} ms against {StutterMs} ms, max {MaxMs} ms "
            + "against 100 ms, CPU {CpuPercent} %.")]
    internal static partial void FrameBudgetMissed(
        ILogger logger,
        string screenName,
        string missing,
        double medianMs,
        double budgetMs,
        double p95Ms,
        double stutterMs,
        double maxMs,
        double cpuPercent);

    /// <summary>
    /// The counterpart to <c>OverlayOpened</c>, and it is worth its own line: on a machine that
    /// nobody is looking at, "the table went dark" is otherwise a fact with no explanation. A
    /// screen turned inactive and one merely suppressed look identical from the room (Part 3).
    /// </summary>
    [LoggerMessage(
        EventId = 3015,
        Level = LogLevel.Information,
        Message = "Overlay on {ScreenId} closed.")]
    internal static partial void OverlayClosed(ILogger logger, ScreenId screenId);

    [LoggerMessage(
        EventId = 3016,
        Level = LogLevel.Information,
        Message = "Screen inventory changed - reporting {ScreenCount} screen(s).")]
    internal static partial void ScreensReported(ILogger logger, int screenCount);

    [LoggerMessage(
        EventId = 3017,
        Level = LogLevel.Information,
        Message = "Applied settings for {ScreenCount} screen(s) from the control.")]
    internal static partial void SettingsApplied(ILogger logger, int screenCount);

    /// <summary>
    /// Worth a line although it changes nothing: it is the one grip whose whole result is
    /// something a person has to SEE, so a run where nobody saw it needs to be told apart from one
    /// where nothing was sent. The count says which - a screen without an overlay shows nothing.
    /// </summary>
    [LoggerMessage(
        EventId = 3018,
        Level = LogLevel.Information,
        Message = "Showing the screen name on {ScreenCount} overlay(s).")]
    internal static partial void ScreensIdentified(ILogger logger, int screenCount);

    /// <summary>
    /// Both directions are worth the same line, and the second one more than the first: a table
    /// that darkens in the middle of a scene is the fault this exists to prevent, and from the
    /// room a device that was TOLD to let go looks exactly like one that failed to hold on.
    /// <para>
    /// Display range although the request is a process-wide flag: the range follows the subject of
    /// the sentence, and the subject here is whether a screen stays lit (Part 8).
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 3019,
        Level = LogLevel.Information,
        Message = "Screen wake lock held: {Holding}.")]
    internal static partial void WakeLockChanged(ILogger logger, bool holding);

    /// <summary>
    /// Said out loud on purpose: a development run and an installed copy differ in exactly this
    /// one path, and a run that quietly used the wrong root would be indistinguishable from a
    /// correct one until it had already touched the DM's own configuration (Part 9).
    /// <para>
    /// 4000–4999 is the operations range - what the process does to itself. It is the fourth
    /// range beside connection, assets and display, and it exists because a data root is none
    /// of those three (Part 8).
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Data root: {Path}")]
    internal static partial void DataRootChosen(ILogger logger, string path);

    [LoggerMessage(
        EventId = 4005,
        Level = LogLevel.Information,
        Message = "No display.json yet - created one, device {DeviceId}.")]
    internal static partial void ConfigurationCreated(ILogger logger, Guid deviceId);

    /// <summary>
    /// A warning, not a note: this costs the device its identity, and it will introduce itself
    /// to the control as a new, unpaired device (Part 6).
    /// </summary>
    [LoggerMessage(
        EventId = 4006,
        Level = LogLevel.Warning,
        Message = "display.json was unreadable. Set aside as {SetAside}; this device starts with "
                  + "a new identity and has to be paired again - or reassigned in the control.")]
    internal static partial void ConfigurationReplaced(ILogger logger, string setAside);

    /// <summary>
    /// Connection range, 1033 onwards: pairing seen from the device. Same range as the hub's,
    /// because it is the same subject - who is talking to whom (Part 8).
    /// </summary>
    [LoggerMessage(
        EventId = 1033,
        Level = LogLevel.Information,
        Message = "Waiting for the DM to allow this device. Pairing code {PairingCode}.")]
    internal static partial void PairingPending(ILogger logger, string pairingCode);

    [LoggerMessage(
        EventId = 1034,
        Level = LogLevel.Information,
        Message = "Paired with control {ControlId}; the token is stored.")]
    internal static partial void Paired(ILogger logger, Guid controlId);

    /// <summary>
    /// The binding is deliberately NOT dropped on this. Doing it automatically would turn a
    /// convenience into an attack: the beacon is unauthenticated, so a forged control that
    /// answers every Hello this way would unbind every display in the house and could then adopt
    /// them itself. It takes a tap at the device - and that tap is the hurdle an attacker on the
    /// network cannot take (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1035,
        Level = LogLevel.Warning,
        Message = "This control does not know this device any more. The pairing stays until it is "
                  + "reset AT the device.")]
    internal static partial void TokenUnknown(ILogger logger);

    [LoggerMessage(
        EventId = 1036,
        Level = LogLevel.Warning,
        Message = "The DeviceId collided with a device that is already connected - taking a fresh "
                  + "identity {DeviceId} and pairing again.")]
    internal static partial void FreshIdentityTaken(ILogger logger, Guid deviceId);

    [LoggerMessage(
        EventId = 1037,
        Level = LogLevel.Warning,
        Message = "The control turned this device away: {Reason}.")]
    internal static partial void PairingRefused(ILogger logger, string reason);

    /// <summary>
    /// Written by the application rather than by Transport, because the waiting belongs to the
    /// loop that decides WHETHER to try again - which is this one. Same range either way: who is
    /// talking to whom (Part 8).
    /// </summary>
    [LoggerMessage(
        EventId = 1042,
        Level = LogLevel.Information,
        Message = "Trying again in {Delay}.")]
    internal static partial void RetryingIn(ILogger logger, TimeSpan delay);

    /// <summary>
    /// The same last word as the control's 4011, with its own number because the ranges are global
    /// and the two say different things about different machines. On a display PC it matters more:
    /// nobody is sitting in front of it to see the process go.
    /// </summary>
    [LoggerMessage(
        EventId = 4012,
        Level = LogLevel.Critical,
        Message = "Unhandled fault on {Where} - this display is going down.")]
    internal static partial void UnhandledFault(ILogger logger, Exception exception, string where);

    /// <summary>
    /// The loop that looks for a control died. Nothing else in this process notices - the windows
    /// stay, the scene stays, and the device simply never connects again - so this line is the only
    /// thing standing between a silent failure and a fault somebody can act on. On a machine nobody
    /// is sitting at, a silence is the worst shape a fault can take (Part 1).
    /// </summary>
    [LoggerMessage(
        EventId = 1049,
        Level = LogLevel.Error,
        Message = "The search for a control ended unexpectedly - this device will not connect again "
                  + "until it is restarted.")]
    internal static partial void ConnectionLoopFailed(ILogger logger, Exception exception);
}
