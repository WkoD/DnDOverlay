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

    [LoggerMessage(
        EventId = 1015,
        Level = LogLevel.Information,
        Message = "Listening on UDP {Port} for a control.")]
    internal static partial void ListeningForControls(ILogger logger, int port);

    [LoggerMessage(
        EventId = 1016,
        Level = LogLevel.Information,
        Message = "Heard control {Name} at {Address}, port {Port}.")]
    internal static partial void ControlHeard(ILogger logger, string name, System.Net.IPAddress address, int port);

    /// <summary>
    /// A paired display discards foreign beacons - the binding is to the control, not to an
    /// address. Debug rather than information: in a household with two controls this would
    /// otherwise be a line every two seconds (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1017,
        Level = LogLevel.Debug,
        Message = "Ignoring control {ControlId} ({Name}) - this device belongs to another one.")]
    internal static partial void ForeignControlIgnored(ILogger logger, Guid controlId, string name);

    /// <summary>
    /// Not fatal: the way through is the host by hand, which is a documented path and not a
    /// stopgap (Part 4, Part 9).
    /// </summary>
    [LoggerMessage(
        EventId = 1018,
        Level = LogLevel.Warning,
        Message = "Could not listen on UDP {Port}; a host will have to be given by hand.")]
    internal static partial void ListeningFailed(ILogger logger, Exception exception, int port);
}
