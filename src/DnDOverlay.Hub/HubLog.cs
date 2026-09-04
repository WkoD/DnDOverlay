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
/// Next free in the connection range: <b>1051</b>; in the display range, which the screen
/// inventory writes into because the range follows the SUBJECT rather than the assembly:
/// <b>3023</b>. 1007–1009 stay unassigned so the first block could still
/// grow, 1010–1018 belong to Transport with 1019 left free, pairing has 1020–1037 (the display
/// writes 1033–1037 - the range follows the subject, not the assembly), discovery has 1038–1041,
/// the display's backoff 1042, the send side 1043–1045, log forwarding 1046–1047, the stranger
/// heard by Transport 1048 and the display's connection loop 1049. <b>1050 belongs to discovery
/// and does not adjoin it</b> - a number is never moved to keep a range tidy, because the number
/// is the contract and tidiness is not. The catalogue lives in <c>docs/protocol.md</c>.
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

    /// <summary>
    /// Named by address, not by device: the send loop exists from the moment the socket is
    /// accepted, so at this point there may not be a device yet (Part 3, Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "The connection to {Address} ended while sending.")]
    internal static partial void SendFailed(ILogger logger, Exception exception, string address);

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

    [LoggerMessage(
        EventId = 1038,
        Level = LogLevel.Information,
        Message = "Announcing this control on UDP {Port}, every couple of seconds.")]
    internal static partial void BeaconStarted(ILogger logger, int port);

    /// <summary>
    /// Where the beacon goes, written only when it changes.
    /// <para>
    /// <b>Information, not Debug</b> - and the reason is the change filter itself. Debug was the
    /// first answer, on the grounds that the beacon repeats every two seconds; but once the line
    /// is written only when the set of addresses moves, it cannot repeat at all, and it shares
    /// that property with 1038 and 1039, which are Information for exactly the same reason. It
    /// also has to be: the control gates its own file at Information and has no setting to lower
    /// it, so a Debug line here would be one nobody could ever read (Part 8).
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 1050,
        Level = LogLevel.Information,
        Message = "Beacon now goes out over {Count} address(es): {Targets}.")]
    internal static partial void BeaconTargetsChanged(ILogger logger, int count, string targets);

    [LoggerMessage(
        EventId = 1039,
        Level = LogLevel.Information,
        Message = "The discovery beacon has stopped.")]
    internal static partial void BeaconStopped(ILogger logger);

    /// <summary>
    /// Debug, not warning: on a machine with VPN or Hyper-V adapters this is the ordinary state
    /// of affairs, and the others still carry us. A warning here would cry wolf every two seconds.
    /// </summary>
    [LoggerMessage(
        EventId = 1040,
        Level = LogLevel.Debug,
        Message = "The beacon could not go out over {Address}.")]
    internal static partial void BeaconInterfaceFailed(ILogger logger, Exception exception, System.Net.IPAddress address);

    /// <summary>
    /// This one IS a warning, and it is the one that matters: not a single interface took the
    /// beacon, so no display will ever find this control by itself. The way out is the host by
    /// hand (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1041,
        Level = LogLevel.Warning,
        Message = "The beacon reached no interface at all - displays will have to be given a host by hand.")]
    internal static partial void BeaconReachedNobody(ILogger logger);

    /// <summary>
    /// Not "a message was dropped": the state queue never drops anything, so a full one says the
    /// counterpart has stopped taking what it is given and this connection can no longer be held
    /// consistent. It is closed, and the ordinary reconnect puts the truth back (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1043,
        Level = LogLevel.Warning,
        Message = "{Address} is not taking what it is given ({Queued} messages, {Bytes} bytes queued) - closing.")]
    internal static partial void StateQueueFull(ILogger logger, string address, int queued, long bytes);

    [LoggerMessage(
        EventId = 1044,
        Level = LogLevel.Warning,
        Message = "A write to {Address} did not complete within {Limit} - closing.")]
    internal static partial void WriteTimedOut(ILogger logger, string address, TimeSpan limit);

    /// <summary>
    /// Silence, not a count of unanswered pings: a device that is busy sending is alive whether or
    /// not a <c>Pong</c> happened to cross the wire.
    /// </summary>
    [LoggerMessage(
        EventId = 1045,
        Level = LogLevel.Information,
        Message = "{Address} has said nothing for {Silence} - treating the connection as dead.")]
    internal static partial void HeartbeatLost(ILogger logger, string address, TimeSpan silence);

    /// <summary>
    /// Said once per connection, and only when it is worth saying. An unattended display PC
    /// without internet and with a flat coin cell can be hours out; without this line its
    /// forwarded entries look like they were written at a plausible but wrong time, and its own
    /// diagnostic file - which somebody may hand over later - looks the same (Part 8).
    /// <para>
    /// Measured against the two timestamps a <c>LogEntry</c> carries anyway. There is deliberately
    /// no timestamp on any other message: an absolute foreign clock is not a usable quantity, and
    /// a field carrying one would eventually be used for ordering or for age, both of which break
    /// on exactly the machine this line is about.
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 1046,
        Level = LogLevel.Warning,
        Message = "The clock of {DeviceName} is {Difference} away from ours - its own timestamps read accordingly.")]
    internal static partial void DeviceClockDiffers(ILogger logger, string deviceName, TimeSpan difference);

    /// <summary>
    /// The rate follows the level, because the documented way to look for a fault is to raise a
    /// display to <c>Debug</c> on purpose - a fixed rate would bite precisely when the DM asked
    /// for the flood (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1047,
        Level = LogLevel.Warning,
        Message = "{DeviceName} sent more than {Limit} log entries a second at {Level}; the surplus was dropped.")]
    internal static partial void LogRateExceeded(ILogger logger, string deviceName, int limit, LogLevel level);

    /// <summary>
    /// A device is reporting its fingers faster than any table could produce them. The reports are
    /// refused from here on; said once for the connection, because a line per refusal would be the
    /// flood it is about (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 1053,
        Level = LogLevel.Warning,
        Message = "{DeviceName} is sending more than {Limit} touch reports a second; the rest are refused.")]
    internal static partial void TouchRateExceeded(ILogger logger, string deviceName, int limit);

    /// <summary>
    /// A screen nobody has met. It becomes <c>Enabled</c> like every unknown one, and this is a
    /// plain fact - the way onwards is "reassign screen", should the derivation have moved
    /// (Part 3).
    /// </summary>
    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Information,
        Message = "New screen {Screen} ({Label}) - playing on it.")]
    internal static partial void ScreenAdded(ILogger logger, ScreenRef screen, string label);

    /// <summary>
    /// Gone from the inventory - and expressly WITHOUT a warning about a loss. Nothing goes: the
    /// tile stays, it goes on showing the scene, and "save screen as scene" works unchanged. A
    /// warning about a loss that is not happening would make the other two untrustworthy
    /// (Part 3).
    /// </summary>
    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Information,
        Message = "Screen {Screen} ({Label}) is no longer reported - its scene and its state stay.")]
    internal static partial void ScreenMissing(ILogger logger, ScreenRef screen, string label);

    /// <summary>
    /// The one finding at which something actually breaks, and therefore the only loud one:
    /// clamping and capping are recomputed, items move and shrink - and UNDO does not reach them,
    /// because transformations are not in the timeline (Part 1, idea 6; Part 3).
    /// </summary>
    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Warning,
        Message = "Screen {Label} changed from {Before} to {After} - images on it have been recomputed.")]
    internal static partial void ScreenMetricsChanged(ILogger logger, string label, PixelSize before, PixelSize after);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Information,
        Message = "Screen {Screen} is now {State}.")]
    internal static partial void ScreenStateChanged(ILogger logger, ScreenRef screen, ScreenState state);

    /// <summary>
    /// A finding, never a state. The wish stands untouched next to it, which is what makes the
    /// return trip free of any memory (Part 3).
    /// </summary>
    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Information,
        Message = "Screen {Screen} is not being played on: {Reason}.")]
    internal static partial void ScreenSuppressed(ILogger logger, ScreenRef screen, SuppressReason reason);

    [LoggerMessage(
        EventId = 3012,
        Level = LogLevel.Information,
        Message = "Screen {Screen} can be played on again.")]
    internal static partial void ScreenAvailable(ILogger logger, ScreenRef screen);

    /// <summary>
    /// A device sent a screen wish or a finding. All five states are born in the control and
    /// travel one way; one arriving from the other side is passed over rather than obeyed, and
    /// saying so is the difference between a rule and a silence (Part 3, Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 3013,
        Level = LogLevel.Warning,
        Message = "{DeviceName} sent a screen state of its own - passed over; states belong to the control.")]
    internal static partial void ScreenCommandIgnored(ILogger logger, string deviceName);

    /// <summary>
    /// A device reported a gesture for a screen this connection is not addressed by. The mirror
    /// image of the display's own 3004: each side discards what is not its own and says so, and
    /// between them there is no direction in which a foreign table can be moved (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 3022,
        Level = LogLevel.Warning,
        Message = "{DeviceName} reported a gesture for {ScreenName}, which is not one of its screens.")]
    internal static partial void ForeignScreenRefused(ILogger logger, string deviceName, string screenName);

    /// <summary>
    /// A ring was sent to a table. Debug, because at ten a minute it would otherwise crowd out the
    /// evening - and it earns its place all the same: the ring is drawn at the OTHER end, so
    /// without this line "the control did not send" and "the table did not draw" are the same
    /// picture from here (the lesson of M3c).
    /// </summary>
    [LoggerMessage(
        EventId = 3035,
        Level = LogLevel.Debug,
        Message = "A spotlight was sent to {ScreenName}.")]
    internal static partial void Spotlight(ILogger logger, string screenName);

    /// <summary>
    /// A picture went from one screen to another. It is the one operation that changes two screens
    /// at once, and the evening's question it answers is "where did that go?" - a picture that
    /// vanished from the table is otherwise indistinguishable from one that was removed.
    /// </summary>
    [LoggerMessage(
        EventId = 3033,
        Level = LogLevel.Information,
        Message = "An image moved from {FromScreen} to {ToScreen}.")]
    internal static partial void ItemMoved(ILogger logger, string fromScreen, string toScreen);

    /// <summary>
    /// A picture was copied onto a screen. Worth a line of its own because nothing is transferred:
    /// the asset is already on every device that has the template, so the copy appears without a
    /// single byte on the wire - and that is what the hand-run reads here (Part 11, step 25b).
    /// </summary>
    [LoggerMessage(
        EventId = 3034,
        Level = LogLevel.Information,
        Message = "An image was copied onto {ScreenName}.")]
    internal static partial void ItemCopied(ILogger logger, string screenName);

    /// <summary>
    /// A hand at the table took hold of a locked picture. The display refuses the gesture itself
    /// and gives the finger the same short answer it gives on a disabled screen, so this line is
    /// not the player's feedback - it is the DM's, for the evening when somebody says "that one is
    /// broken" (Part 3).
    /// </summary>
    [LoggerMessage(
        EventId = 3021,
        Level = LogLevel.Information,
        Message = "A locked image on {ScreenName} was not moved from the table.")]
    internal static partial void LockedItemNotMoved(ILogger logger, string screenName);

    /// <summary>
    /// The one exception to "the hub is authoritative", and it is deliberately this narrow: a
    /// control that has just restarted has no scene, the display still has one, and the side that
    /// connects hands it to the side that lost it (Part 1, idea 4; Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 3014,
        Level = LogLevel.Information,
        Message = "Took the scene of {Screen} over from the device: {ItemCount} image(s).")]
    internal static partial void SceneTakenOver(ILogger logger, ScreenRef screen, int itemCount);
}
