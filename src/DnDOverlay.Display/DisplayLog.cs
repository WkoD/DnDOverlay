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
        Message = "Could not load asset {AssetId}.")]
    internal static partial void AssetFailed(ILogger logger, Exception exception, AssetId assetId);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Information,
        Message = "Asset {AssetId} decoded at {Width}×{Height}.")]
    internal static partial void AssetDecoded(ILogger logger, AssetId assetId, int width, int height);

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
}
