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
}
