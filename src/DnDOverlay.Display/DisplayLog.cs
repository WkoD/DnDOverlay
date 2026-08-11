using DnDOverlay.Core;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Display;

/// <summary>
/// The display range, 3000–3999. Numbers are global, strictly ascending within their range and
/// never reused; the catalogue lives in <c>docs/protocol.md</c> (Part 8).
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
}
