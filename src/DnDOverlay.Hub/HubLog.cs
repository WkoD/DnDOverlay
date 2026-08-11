using DnDOverlay.Core;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// The hub's log messages, declared the way Part 8 asks for: a stable numeric
/// <c>EventId</c> with a name, NAMED placeholders rather than positional ones, and the
/// declaration checked at compile time by the source generator.
/// <para>
/// The number is the contract, the name is for reading. Numbers are grouped into ranges -
/// 1000–1999 connection, 2000–2999 assets, 3000–3999 display - and strictly ascending within a
/// range. <b>A retired number is never reused</b>: were 1002 to take on a new meaning, an older
/// counterpart would render a PLAUSIBLE BUT WRONG line from its old catalogue entry, which is
/// worse than an unknown identifier that at least looks unknown.
/// </para>
/// <para>
/// Next free in this range: <b>1007</b>. The catalogue moves to <c>docs/protocol.md</c> with the
/// rest of the protocol.
/// </para>
/// </summary>
internal static partial class HubLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Display {DeviceId} ({DeviceName}) connected with {ScreenCount} screen(s).")]
    internal static partial void DisplayConnected(
        ILogger logger,
        DeviceId deviceId,
        string deviceName,
        int screenCount);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Display {DeviceId} disconnected.")]
    internal static partial void DisplayDisconnected(ILogger logger, DeviceId deviceId);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "A display connected without a Hello and was dropped.")]
    internal static partial void DisplayWithoutHello(ILogger logger);

    /// <summary>
    /// Reported, never enforced. The control is the path along which a display gets updated, so
    /// rejecting it would cut the one wire at the moment it is needed (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Display {DeviceId} speaks protocol {TheirVersion}, we speak {OurVersion} - carrying on.")]
    internal static partial void ProtocolVersionDiffers(
        ILogger logger,
        DeviceId deviceId,
        int theirVersion,
        int ourVersion);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Debug,
        Message = "Ignoring a message from {DeviceId} that this build does not handle.")]
    internal static partial void UnhandledMessageIgnored(ILogger logger, DeviceId deviceId);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "The connection to display {DeviceId} ended while sending.")]
    internal static partial void SendFailed(ILogger logger, Exception exception, DeviceId deviceId);
}
