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
[JsonDerivedType(typeof(SceneSnapshotMessage), "SceneSnapshot")]
[JsonDerivedType(typeof(ScenePatchMessage), "ScenePatch")]
public abstract record ProtocolMessage;

/// <summary>
/// What a display says when it connects. In M1a it carries no token and no scene state: pairing
/// and the state takeover are M1b, and a field that exists but is never honoured is worse than
/// one that is not there yet.
/// </summary>
public sealed record HelloMessage(
    DeviceId DeviceId,
    string Name,
    string AppVersion,
    int ProtocolVersion,
    IReadOnlyList<ScreenInfo> Screens) : ProtocolMessage
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
        && Screens.SequenceEqual(other.Screens);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeviceId);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(AppVersion, StringComparer.Ordinal);
        hash.Add(ProtocolVersion);

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
public sealed record WelcomeMessage(Guid ControlId, string AssetPath) : ProtocolMessage;

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
}
