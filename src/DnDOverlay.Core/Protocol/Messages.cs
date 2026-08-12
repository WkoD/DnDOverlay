using System.Text.Json.Serialization;

namespace DnDOverlay.Core.Protocol;

/// <summary>
/// The JSON envelope every message travels in: <c>{ "t": "&lt;Type&gt;", … }</c>.
/// <para>
/// The C# names carry a <c>Message</c> suffix while the wire names do not. That is not
/// decoration: <see cref="ScenePatch"/> is a model type as well, and two things called the same
/// in one namespace would force an alias at every call site. The WIRE name is the contract, and
/// it is the one that must never change (Part 1, rule 7).
/// </para>
/// <para>
/// Only the messages M1a actually exchanges live here. The remaining two dozen from Part 4
/// arrive with the milestone that implements them - same reasoning as for
/// <see cref="PatchOp"/>: additive is allowed, pretending is not.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(HelloMessage), "Hello")]
[JsonDerivedType(typeof(WelcomeMessage), "Welcome")]
[JsonDerivedType(typeof(PairingPendingMessage), "PairingPending")]
[JsonDerivedType(typeof(RejectedMessage), "Rejected")]
[JsonDerivedType(typeof(PingMessage), "Ping")]
[JsonDerivedType(typeof(PongMessage), "Pong")]
[JsonDerivedType(typeof(SceneSnapshotMessage), "SceneSnapshot")]
[JsonDerivedType(typeof(ScenePatchMessage), "ScenePatch")]
public abstract record ProtocolMessage;

/// <summary>
/// What a display says when it connects.
/// </summary>
/// <param name="Token">
/// The device token from a previous pairing, or <see langword="null"/> while unpaired. It is the
/// whole of the authentication: the hub looks the device up, compares in constant time and lets
/// it straight in - the normal case at every power-on (Part 4).
/// </param>
/// <param name="PairingCode">
/// The four digits the DM compares with what stands on the table, sent while unpaired.
/// <para>
/// It belongs to the REQUEST, not to the connection attempt: the display makes it once and keeps
/// it across drops, or the DM would be comparing a number that changed while he walked over. That
/// is also what lets the hub write one log line per request instead of one per reconnect - an
/// unpaired device on weak Wi-Fi comes back every few seconds (Part 4).
/// </para>
/// </param>
public sealed record HelloMessage(
    DeviceId DeviceId,
    string Name,
    string AppVersion,
    int ProtocolVersion,
    IReadOnlyList<ScreenInfo> Screens,
    string? Token = null,
    string? PairingCode = null) : ProtocolMessage
{
    /// <summary>
    /// Structural over the screen list, for the same reason <see cref="SceneState"/> is: a record
    /// compares list members by REFERENCE, so a message that went through the wire would never
    /// equal the one that was sent. The round-trip test in Part 11 is what makes that visible -
    /// and it is the guard for every DTO added later.
    /// </summary>
    public bool Equals(HelloMessage? other) =>
        other is not null
        && DeviceId == other.DeviceId
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(AppVersion, other.AppVersion, StringComparison.Ordinal)
        && ProtocolVersion == other.ProtocolVersion
        && string.Equals(Token, other.Token, StringComparison.Ordinal)
        && string.Equals(PairingCode, other.PairingCode, StringComparison.Ordinal)
        && Screens.SequenceEqual(other.Screens);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeviceId);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(AppVersion, StringComparer.Ordinal);
        hash.Add(ProtocolVersion);
        hash.Add(Token, StringComparer.Ordinal);
        hash.Add(PairingCode, StringComparer.Ordinal);

        foreach (var screen in Screens)
        {
            hash.Add(screen);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// The control's answer.
/// </summary>
/// <param name="AssetPath">
/// A PATH, never an absolute URL, and never host and port. Those come from the socket the
/// message arrived on. A remembered base URL is a trap: when the Surface moves between WLAN and
/// dock, the WebSocket finds the new address by itself while the base URL still points at the
/// old one - the display would be CONNECTED and load nothing, shown as "asset still loading"
/// (Part 4, Part 5).
/// </param>
/// <param name="Token">
/// A freshly issued device token, present exactly once: in the answer to the pairing the DM just
/// allowed. On every later connection the display brings its own and this stays
/// <see langword="null"/>.
/// </param>
public sealed record WelcomeMessage(Guid ControlId, string AssetPath, string? Token = null) : ProtocolMessage;

/// <summary>
/// "It is with the DM." Sent the moment a request starts waiting, and it is what makes the
/// display put its setup screen down - with name, address and pairing code, big enough to read
/// from two metres (Part 6).
/// <para>
/// Nothing expires. The request stands as long as the connection stands and vanishes with it, so
/// what is in the list is what is knocking right now. A deadline would have the opposite fault:
/// the DM steps out, comes back, and the request is gone without anyone having decided anything
/// (Part 4).
/// </para>
/// </summary>
public sealed record PairingPendingMessage(string PairingCode) : ProtocolMessage;

/// <summary>Why a connection was turned away. Four reasons, and they are told apart at the device.</summary>
public enum RejectionReason
{
    /// <summary>The DM said no - or new devices are not being accepted at the moment.</summary>
    Denied,

    /// <summary>
    /// The token is not one we know. The display does NOT drop its binding on its own: the beacon
    /// is unauthenticated, so a forged control plus this answer would unbind every display in the
    /// house and let the attacker adopt them. It asks at the device instead (Part 4).
    /// </summary>
    InvalidToken,

    /// <summary>A rate or capacity limit. Shown in the device list rather than swallowed.</summary>
    LimitExceeded,

    /// <summary>
    /// Another device is live under this <c>DeviceId</c> - the ordinary result of cloning a disk
    /// to set up a second display PC. The device answers by making itself a fresh identity and
    /// pairing regularly, which is why this is not a dead end (Part 4, Part 7).
    /// </summary>
    DuplicateDevice,
}

/// <summary>The refusal itself. It ends the connection; the reason stays visible in the control.</summary>
public sealed record RejectedMessage(RejectionReason Reason) : ProtocolMessage;

/// <summary>
/// Heartbeat, and the probe that tells a clone from a fast restart.
/// <para>
/// A second connection with a valid token looks exactly like a crashed display coming straight
/// back. The hub therefore asks the connection it already has and gives it a second: silence means
/// it was the same machine and gets replaced, an answer means there are two - decided on an
/// ANSWER, not on a deadline (Part 4).
/// </para>
/// </summary>
/// <param name="RoundTripMs">
/// The last round trip the control measured, carried back so both sides show the same number
/// instead of measuring it twice in two different ways (Part 4).
/// </param>
public sealed record PingMessage(long? RoundTripMs = null) : ProtocolMessage;

/// <summary>The answer to a <see cref="PingMessage"/>. Carries the battery once M5 needs it.</summary>
public sealed record PongMessage : ProtocolMessage;

/// <summary>The complete scene of one screen. Addressed, like everything, with a <see cref="ScreenRef"/>.</summary>
public sealed record SceneSnapshotMessage(ScreenRef Screen, SceneState Scene) : ProtocolMessage;

/// <summary>One command of the DM, as one patch over possibly several screens.</summary>
public sealed record ScenePatchMessage(ScenePatch Patch) : ProtocolMessage;

/// <summary>Constants that both ends agree on.</summary>
public static class Protocol
{
    /// <summary>
    /// The protocol version. It is REPORTED, never enforced: a mismatch rejects nothing in
    /// either direction, because the control is the very path along which a display gets updated
    /// - rejecting it would cut the one wire at the exact moment it is needed (Part 4).
    /// </summary>
    public const int Version = 1;

    /// <summary>The WebSocket endpoint a display connects to.</summary>
    public const string DisplayPath = "/ws/display";

    /// <summary>Where assets are served. The display gets this in the <see cref="WelcomeMessage"/>.</summary>
    public const string AssetPath = "/assets";

    /// <summary>Reachability probe. Deliberately token free, and it gives away nothing but "running" (Part 4).</summary>
    public const string HealthPath = "/health";

    /// <summary>The default port Kestrel binds on, across all interfaces.</summary>
    public const int DefaultPort = 47800;

    /// <summary>
    /// The UDP port the discovery beacon goes out on. The same number as the TCP one and a
    /// different protocol, so nothing collides - and one number to remember instead of two.
    /// <para>
    /// It does NOT follow a changed <see cref="DefaultPort"/> in configuration: a display that has
    /// never spoken to this control cannot know a port that was chosen there, so the one place
    /// they meet has to be fixed. Which port to connect to afterwards is in the beacon.
    /// </para>
    /// </summary>
    public const int DiscoveryPort = 47800;

    /// <summary>How often the beacon goes out. A machine set up at the table should appear at once.</summary>
    public static readonly TimeSpan BeaconInterval = TimeSpan.FromSeconds(2);
}
