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
}
