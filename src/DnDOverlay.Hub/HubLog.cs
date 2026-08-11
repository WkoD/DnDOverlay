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
/// Next free in this range: <b>1033</b>. 1007–1009 stay unassigned so the first block could still
/// grow, 1015–1019 belong to Transport, and pairing has 1020–1031 to itself. The catalogue lives
/// in <c>docs/protocol.md</c>.
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

    /// <summary>
    /// Written once per pairing code, never once per connection. An unpaired device on weak Wi-Fi
    /// loses its connection and comes back every few seconds; because the code survives that, the
    /// same request updates instead of writing a second line. What the DM looks for later is not
    /// a notification but the trail: <i>did this device ever knock, and what came of it?</i>
    /// (Part 4)
    /// </summary>
    [LoggerMessage(
        EventId = 1020,
        Level = LogLevel.Information,
        Message = "Pairing request from {DeviceName} at {Address}, code {PairingCode} ({DeviceId}).")]
    internal static partial void PairingRequested(
        ILogger logger,
        DeviceId deviceId,
        string deviceName,
        string address,
        string pairingCode);

    [LoggerMessage(
        EventId = 1021,
        Level = LogLevel.Information,
        Message = "Pairing allowed for {DeviceName} ({DeviceId}) as {Role}.")]
    internal static partial void PairingApproved(
        ILogger logger,
        DeviceId deviceId,
        string deviceName,
        PairingRole role);

    [LoggerMessage(
        EventId = 1022,
        Level = LogLevel.Information,
        Message = "Pairing denied for {DeviceName} ({DeviceId}).")]
    internal static partial void PairingDenied(ILogger logger, DeviceId deviceId, string deviceName);

    /// <summary>The device went away while the request was standing. Nothing is left behind.</summary>
    [LoggerMessage(
        EventId = 1023,
        Level = LogLevel.Debug,
        Message = "The pairing request from {DeviceId} withdrew itself - the connection ended.")]
    internal static partial void PairingWithdrawn(ILogger logger, DeviceId deviceId);

    /// <summary>
    /// The display does NOT drop its binding on this - it asks at the device (Part 4). What this
    /// line is for is the device list, where the reason stands in plain words instead of the
    /// device simply disappearing.
    /// </summary>
    [LoggerMessage(
        EventId = 1024,
        Level = LogLevel.Warning,
        Message = "{DeviceName} ({DeviceId}) presented a token this control does not know.")]
    internal static partial void TokenRefused(ILogger logger, DeviceId deviceId, string deviceName);

    [LoggerMessage(
        EventId = 1025,
        Level = LogLevel.Information,
        Message = "{DeviceName} at {Address} knocked while new devices are not being accepted.")]
    internal static partial void NewDevicesBlocked(ILogger logger, string deviceName, string address);

    /// <summary>
    /// Refused and shown in the device list, never swallowed. These limits keep the process
    /// alive; they are not the access control - the token is (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1026,
        Level = LogLevel.Warning,
        Message = "{DeviceName} at {Address} was turned away: {Limit}.")]
    internal static partial void LimitReached(
        ILogger logger,
        string deviceName,
        string address,
        string limit);

    [LoggerMessage(
        EventId = 1027,
        Level = LogLevel.Warning,
        Message = "{DeviceName} ({DeviceId}) is a clone - the connection under that identity answered. Asking the DM.")]
    internal static partial void CloneDetected(ILogger logger, DeviceId deviceId, string deviceName);

    [LoggerMessage(
        EventId = 1028,
        Level = LogLevel.Information,
        Message = "Display {DeviceId} reconnected; the previous connection was silent and has been replaced.")]
    internal static partial void ConnectionReplaced(ILogger logger, DeviceId deviceId);

    [LoggerMessage(
        EventId = 1029,
        Level = LogLevel.Information,
        Message = "{DeviceName} ({DeviceId}) was told to take a fresh identity and pair again.")]
    internal static partial void FreshIdentityRequested(ILogger logger, DeviceId deviceId, string deviceName);

    [LoggerMessage(
        EventId = 1030,
        Level = LogLevel.Information,
        Message = "Pairing withdrawn for {DeviceId} - its token no longer opens anything.")]
    internal static partial void Unpaired(ILogger logger, DeviceId deviceId);

    [LoggerMessage(
        EventId = 1031,
        Level = LogLevel.Information,
        Message = "The rejection of {DeviceId} was taken back; its next attempt is an ordinary request.")]
    internal static partial void RejectionCleared(ILogger logger, DeviceId deviceId);

    /// <summary>
    /// Ignored and logged, never fatal - that is what lets an older display face a newer control
    /// at all (rule 7). It is read before the Hello, so there is no device to name yet.
    /// </summary>
    [LoggerMessage(
        EventId = 1032,
        Level = LogLevel.Debug,
        Message = "A message this build does not understand was ignored.")]
    internal static partial void MessageIgnored(ILogger logger);
}
