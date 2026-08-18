using System.Text.Json.Serialization;
using DnDOverlay.Core.Logging;
using Microsoft.Extensions.Logging;

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
[JsonDerivedType(typeof(LogEntryMessage), "LogEntry")]
[JsonDerivedType(typeof(ScreensChangedMessage), "ScreensChanged")]
[JsonDerivedType(typeof(ConfigUpdateMessage), "ConfigUpdate")]
[JsonDerivedType(typeof(IdentifyScreensMessage), "IdentifyScreens")]
[JsonDerivedType(typeof(AssetProgressMessage), "AssetProgress")]
[JsonDerivedType(typeof(ItemTransformedMessage), "ItemTransformed")]
public abstract record ProtocolMessage;

/// <summary>
/// One screen's arrangement as the device still has it, carried in the <c>Hello</c>.
/// <para>
/// A bare <see cref="ScreenId"/> is enough here and correct: the <c>Hello</c> comes over the
/// device's own socket and is never relayed, so the device is the sender rather than something
/// that has to be stated. On a PATCH it would be wrong, because <c>/ws/control</c> carries every
/// device over one connection (Part 4).
/// </para>
/// </summary>
public sealed record ScreenScene(ScreenId Screen, SceneState Scene);

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
/// <param name="Settings">
/// The full EFFECTIVE parameter set of this device and its screens - the baseline of the
/// two-sided configuration. Without it the same value would have two writers and no reconciling,
/// and the side that spoke last would quietly win. The control takes this over into its copy and
/// only then sends out what it changed while the device was away, so per key the value set last
/// holds and nobody overruns something they never touched (Part 4, Part 6).
/// <para>
/// Additive and optional (rule 7): an older display sends none, and then nothing is taken over.
/// </para>
/// </param>
/// <param name="Scenes">
/// What this device still has on its screens. It is here because a control that has just
/// restarted TAKES IT OVER - the state is written down nowhere, and it survives almost every
/// failure because whichever side connects hands it to the one that lost it (Part 1, idea 4;
/// Part 4). The hub takes over only for screens it has no state of its own for; where it has one,
/// it puts it through with a snapshot.
/// <para>
/// The screen STATES are expressly not in here. All five are born in the control (Part 3).
/// </para>
/// </param>
public sealed record HelloMessage(
    DeviceId DeviceId,
    string Name,
    string AppVersion,
    int ProtocolVersion,
    IReadOnlyList<ScreenInfo> Screens,
    string? Token = null,
    string? PairingCode = null,
    ConfigUpdate? Settings = null,
    IReadOnlyList<ScreenScene>? Scenes = null) : ProtocolMessage
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
        && Settings == other.Settings
        && Screens.SequenceEqual(other.Screens)
        && (Scenes is null
            ? other.Scenes is null
            : other.Scenes is not null && Scenes.SequenceEqual(other.Scenes));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeviceId);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(AppVersion, StringComparer.Ordinal);
        hash.Add(ProtocolVersion);
        hash.Add(Token, StringComparer.Ordinal);
        hash.Add(PairingCode, StringComparer.Ordinal);
        hash.Add(Settings);

        foreach (var screen in Screens)
        {
            hash.Add(screen);
        }

        foreach (var scene in Scenes ?? [])
        {
            hash.Add(scene);
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

/// <summary>
/// One log message on its way to where the DM sits. Errors travel to him; as little as possible
/// stays on the display PC, which stands unattended at the table where nobody looks into files
/// (Part 8).
/// <para>
/// It carries a stable identifier and NAMED VALUES, never a finished sentence. A device that sent
/// finished text would make what the control shows depend on the language setting of a foreign
/// machine - and the control renders it in its own language, from the same catalogue, with the
/// three fallback stages behind it.
/// </para>
/// </summary>
/// <param name="At">
/// The device's own clock. It is the ONE absolute foreign time in the whole protocol, and it is
/// measured against ours the moment it arrives: an unattended machine without internet and with a
/// flat coin cell can be hours out. Everywhere else - touch point age, round trip, silence
/// deadline - time is relative for exactly that reason, because a wrong clock would otherwise
/// produce a plausible and wrong answer about the world instead of about itself.
/// </param>
/// <param name="Screen">
/// The screen this is about, where there is one. Optional and additive (rule 7): an entry without
/// it belongs to the device, an older display sends none at all, and an identifier the control
/// does not know falls back to the device rather than being discarded (Part 8).
/// </param>
public sealed record LogEntryMessage(
    int EventId,
    string EventName,
    LogLevel Level,
    DateTimeOffset At,
    IReadOnlyList<LogValue> Values,
    string? RawText = null,
    ScreenId? Screen = null) : ProtocolMessage
{
    /// <summary>
    /// Structural over the value list, for the same reason <see cref="HelloMessage"/> is: a record
    /// compares list members by reference, so a message that went through the wire would never
    /// equal the one that was sent - and the round-trip test is what makes that visible.
    /// </summary>
    public bool Equals(LogEntryMessage? other) =>
        other is not null
        && EventId == other.EventId
        && string.Equals(EventName, other.EventName, StringComparison.Ordinal)
        && Level == other.Level
        && At == other.At
        && string.Equals(RawText, other.RawText, StringComparison.Ordinal)
        && Screen == other.Screen
        && Values.SequenceEqual(other.Values);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EventId);
        hash.Add(EventName, StringComparer.Ordinal);
        hash.Add(Level);
        hash.Add(At);
        hash.Add(RawText, StringComparer.Ordinal);
        hash.Add(Screen);

        foreach (var value in Values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// A new screen inventory after a hot-plug or a resolution change.
/// <para>
/// It carries facts and nothing else - the same list the <c>Hello</c> brings, for the same reason
/// it does: a display reports which screens it HAS and knows no "unavailable". Which of them is
/// missing is worked out on the control side, by comparing against what it last saw (Part 3).
/// </para>
/// </summary>
public sealed record ScreensChangedMessage(IReadOnlyList<ScreenInfo> Screens) : ProtocolMessage
{
    public bool Equals(ScreensChangedMessage? other) =>
        other is not null && Screens.SequenceEqual(other.Screens);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var screen in Screens)
        {
            hash.Add(screen);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Changed display parameters, screen names, the screen wish and the transient finding - and the
/// one message in the whole protocol that travels in BOTH directions.
/// <para>
/// It has to, because the same value has two writers: Part 6 requires every setting to be
/// reachable at the device as well, and four of them act in the hub rather than at the device. A
/// <c>ParkEdge</c> changed at the table that the control knew nothing about would mean the hub
/// computing against the old value while the device renders against the new - and the
/// re-sorting that the change should trigger not happening at all. The report is therefore the
/// order itself (Part 4).
/// </para>
/// <para>
/// The counter-check that explains the whole sign of this: a device that has never seen a control
/// is fully settable at the table and keeps its settings across restarts. Were it to lose them at
/// the first <c>ConfigUpdate</c>, local operation would be a sham.
/// </para>
/// </summary>
public sealed record ConfigUpdateMessage(ConfigUpdate Update) : ProtocolMessage;

/// <summary>
/// "Say which one you are": every overlay of this device shows its effective name, large, for a
/// few seconds - the same text that stands on its tile (Part 6).
/// <para>
/// It carries nothing. The device knows its own screens and what each of them is called, and a
/// list from the control would be a second copy of the names that could disagree with the first.
/// </para>
/// <para>
/// <b>State rather than transient</b>, unlike the pulse it otherwise resembles. Transient exists
/// to protect rank 1 while the table is busy; this is pressed while a room is being set up, when
/// nothing is going on at all - and a press that silently does nothing is worse than a late one.
/// </para>
/// </summary>
public sealed record IdentifyScreensMessage : ProtocolMessage;

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

/// <summary>
/// Where one picture has got to on its way to a device.
/// </summary>
public enum AssetLoadState
{
    /// <summary>Bytes are coming down.</summary>
    Loading,

    /// <summary>All bytes are here and the delivered hash is being checked (Part 5).</summary>
    Verifying,

    /// <summary>Being turned into a bitmap. Not free, and not instant on a large picture.</summary>
    Decoding,

    /// <summary>Ready to draw - and only then.</summary>
    Done,

    /// <summary>
    /// Finally unsuccessful. A state of its own rather than a ring that never fills: the item shows
    /// a placeholder with a reason instead (Part 7).
    /// </summary>
    Failed,
}

/// <summary>One picture on its way, as a fraction and a state.</summary>
/// <param name="Fraction">
/// Between 0 and 1, and it never goes backwards within one attempt. A retry does not secretly
/// start over either - it continues to be reported as the same attempt, because a ring that jumped
/// back to zero would read as "this is going wrong" when it is merely going slowly (Part 7).
/// </param>
public sealed record AssetLoad(AssetId Asset, double Fraction, AssetLoadState State);

/// <summary>
/// What this device is loading right now - one entry per picture, and the thing the progress ring
/// on the item is fed from (Part 7).
/// <para>
/// It flows <b>without being asked for</b>, because the ring has to be there; and it flows
/// <b>only while something is loading</b>. A device with nothing to do sends nothing at all, and
/// that is the normal case (Part 4).
/// </para>
/// <para>
/// <b>It carries no device identifier.</b> The hub knows which connection it arrived on, and that
/// is the one answer that cannot be forged - a device naming itself here would be a second source
/// for a question that already has a better one.
/// </para>
/// <para>
/// <b>Rank 3</b>, in a queue of its own. Under load the touch points stop getting a turn while
/// this still does: otherwise the first thing to fall away would be the very feedback that
/// explains the load (Part 4).
/// </para>
/// </summary>
public sealed record AssetProgressMessage(IReadOnlyList<AssetLoad> Loads) : ProtocolMessage
{
    public bool Equals(AssetProgressMessage? other) =>
        other is not null && Loads.SequenceEqual(other.Loads);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var load in Loads)
        {
            hash.Add(load);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// What a player did to a picture at the table: pushed it, zoomed it, turned it. It is an
/// INTENTION and not a fact - the hub hands out the revision and distributes the result, which is
/// what makes the order globally unambiguous with several hands on several tables (Part 4).
/// <para>
/// Throttled per ITEM at about 20 Hz and sent once more, bindingly, when the fingers leave. Per
/// item rather than globally: two pictures moved at once would otherwise slow each other down,
/// and the throttling happens BEFORE the queue, because throttling is a decision and dropping is
/// an emergency measure (Part 4).
/// </para>
/// </summary>
/// <param name="Screen">
/// A bare <see cref="ScreenId"/> is right here: this travels over the device's own socket, so the
/// hub knows the device from the connection - the one answer a device cannot get wrong.
/// </param>
/// <param name="KnownRevision">
/// The revision the display had for this item when it took hold of it. The hub does not weigh it
/// against anything - a finger on the table is the most recent truth there is - but it says
/// whether the picture that was grabbed was the current one, and that is the only way a lost
/// patch shows up as anything other than "the table jumped".
/// </param>
/// <param name="Grabbed">
/// True on the first report of a gesture. It is what brings the picture to the front: the display
/// raises it locally the moment it is touched and takes the binding number from the hub right
/// afterwards (Part 3), and a flag on the message it is already sending beats a second message
/// that could arrive in the wrong order.
/// </param>
public sealed record ItemTransformedMessage(
    ScreenId Screen,
    ItemTransform Transform,
    long KnownRevision,
    bool Grabbed) : ProtocolMessage;
