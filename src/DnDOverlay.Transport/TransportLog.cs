using Microsoft.Extensions.Logging;

namespace DnDOverlay.Transport;

/// <summary>
/// The display side of the connection range. Numbers are global and never reused; the catalogue
/// lives in <c>docs/protocol.md</c> (Part 8).
/// </summary>
internal static partial class TransportLog
{
    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Information,
        Message = "Connecting to {HubUri}.")]
    internal static partial void Connecting(ILogger logger, Uri hubUri);

    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Information,
        Message = "Connected to control {ControlId}; assets are served from {AssetPath}.")]
    internal static partial void Connected(ILogger logger, Guid controlId, string assetPath);

    [LoggerMessage(
        EventId = 1012,
        Level = LogLevel.Information,
        Message = "The connection to {HubUri} ended.")]
    internal static partial void Disconnected(ILogger logger, Uri hubUri);

    [LoggerMessage(
        EventId = 1013,
        Level = LogLevel.Warning,
        Message = "Could not reach {HubUri}.")]
    internal static partial void ConnectFailed(ILogger logger, Exception exception, Uri hubUri);

    /// <summary>
    /// Rule 7 in practice: an unknown message is ignored and logged, never fatal. Without this
    /// line the tolerance would be indistinguishable from a bug.
    /// </summary>
    [LoggerMessage(
        EventId = 1014,
        Level = LogLevel.Debug,
        Message = "Ignoring a message this build does not know.")]
    internal static partial void UnknownMessageIgnored(ILogger logger, Exception exception);
}
