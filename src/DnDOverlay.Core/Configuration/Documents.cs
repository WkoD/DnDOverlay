using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core.Configuration;

/// <summary>
/// <c>control.json</c> - what the DM side keeps. Half of it is pure view state; the other half
/// is what the hub is built with (Part 7).
/// <para>
/// The file has two consumers and still exactly one owner: the control. The hub gets a SNAPSHOT
/// at construction and no way back into the file - what it changes, the control sees as an event
/// and writes. Two writers on a debounced, atomically written file would be one too many, and
/// which half won would be decided by the accident of the moment.
/// </para>
/// <para>
/// It grows with the milestones: known devices and tokens arrive with pairing, the screen wishes
/// and the ScreenContext per screen with the screen inventory, the view state with the stage.
/// </para>
/// <para>
/// <b>Plain setters rather than <c>init</c>, and that is not a style slip.</b> With <c>init</c>
/// the JSON source generator builds the object through an object initializer - <c>ObjectCreator</c>
/// is null and every property is marked <c>IsMemberInitializer</c> - which assigns EVERY member,
/// including the ones the file does not mention. Property initializers are then dead: a missing
/// <c>controlId</c> read back as <c>Guid.Empty</c>, a missing list as null, a missing enum as its
/// first value. That takes the ground out from under everything additive (rule 7), because a
/// build that adds a setting meets older files on every machine that updates. Measured, not
/// deduced; guarded by <c>ConfigurationDefaultsTests</c>. The price is a mutable record, and it is
/// the cheaper half of the trade.
/// </para>
/// </summary>
public sealed record ControlConfiguration : IConfigurationDocument
{
    /// <inheritdoc/>
    public int SchemaVersion { get; set; } = ConfigurationSchema.Version;

    /// <summary>
    /// Identifies this control for as long as it exists. Created once, then never again - a
    /// display stays bound to it, and the address will not do for that (Part 4).
    /// </summary>
    public Guid ControlId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The TCP port Kestrel binds on all interfaces.
    /// <para>
    /// It lives here rather than in the code because it is the one value with no other home: a
    /// taken port is reported at startup with its number and a suggestion, and being able to
    /// act on that requires somewhere to put the new one (Part 4, Part 7).
    /// </para>
    /// </summary>
    public int Port { get; set; } = Protocol.Protocol.DefaultPort;

    /// <summary>
    /// Every device the DM has ever allowed, with its token. This is the file the whole pairing
    /// hangs on: without it every display would introduce itself as a stranger after one restart
    /// of the control (Part 4, Part 7).
    /// </summary>
    public IReadOnlyList<KnownDevice> KnownDevices { get; set; } = [];

    /// <summary>
    /// Every screen the control has ever been told about, with the DM's wish and everything a
    /// computation over its scene needs.
    /// <para>
    /// This carries a promise that could not be kept otherwise: a screen is fully playable in
    /// every state and on every finding - expressly including while its device is switched OFF.
    /// Were size, DPI and the display parameters only ever to arrive in the <c>Hello</c>, the hub
    /// could neither place nor cap for an absent device, and preparing the next scene ahead would
    /// fall away (Part 3, Part 7).
    /// </para>
    /// </summary>
    public IReadOnlyList<KnownScreen> KnownScreens { get; set; } = [];

    /// <summary>
    /// How much this control writes into its own file - the counterpart to
    /// <see cref="DisplayConfiguration.Device"/>'s level, and it exists for the same reason.
    /// <para>
    /// Without it the control is the one process whose level cannot be moved at all: a display can
    /// be raised to <see cref="LogLevel.Debug"/> from here or at the device, while the control
    /// itself sits at Information for ever. That is not symmetry for its own sake - it decides
    /// whether a line written at Debug can ever be read, and a line nobody can read is one that
    /// was delivered dead (Part 8).
    /// </para>
    /// <para>
    /// <b>One value, not the device's three.</b> <c>ForwardAtLeast</c> has no meaning here - the
    /// control forwards nothing, it receives - and keeping a screen awake is a display's business.
    /// Reusing <c>DeviceSettings</c> would put two keys into this file that mean nothing in it.
    /// </para>
    /// <para>
    /// Additive: a file written before this existed simply has no such key and reads as
    /// Information, so the schema version stays where it is.
    /// </para>
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// The pattern for the counted name - stage 5 of the naming, used when a picture arrives with
    /// nothing to derive a name from (Part 3, Part 7).
    /// <para>
    /// <b>It is a setting rather than a constant for one reason above all:</b> hard wired it would
    /// be the single place where a German word stood in an English interface or the other way
    /// round - and the DM who would rather have "NSC 7" could not reach it. The
    /// <c>{n}</c> is where the number goes; a pattern without it is taken at its word.
    /// </para>
    /// <para>
    /// Additive, so a file written before this existed simply reads the default and the schema
    /// version stays where it is (rule 7).
    /// </para>
    /// </summary>
    public string CountedNamePattern { get; set; } = "Clipboard {n}";

    /// <summary>
    /// How far the counted names have got. Kept so a restart does not start again at one and
    /// collide with every name from the last session - the stock would number those apart, and the
    /// DM would be looking at "Clipboard 1 (2)" without ever having done anything twice.
    /// </summary>
    public int CountedNameNext { get; set; } = 1;
}

/// <summary>
/// One screen as it sits in <c>control.json</c>: what the device said about it, what the DM
/// wishes, and how it is set.
/// </summary>
/// <param name="State">
/// The WISH, and only ever the wish. The three transient findings - unavailable, the control
/// window lying on it, hidden at the device - are never written here. A finding that overwrote
/// the wish would have to restore it afterwards, and that memory is exactly where such models
/// come apart (Part 3).
/// </param>
/// <param name="Size">
/// Physical pixels, as last reported. A hardware fact rather than a setting, which is why the
/// device always wins on it and a <c>ConfigUpdate</c> cannot touch it.
/// </param>
public sealed record KnownScreen(
    Guid DeviceId,
    string ScreenId,
    string Label,
    ScreenState State,
    PixelSize Size,
    double Dpi,
    ScreenSettings Settings);

/// <summary>
/// One paired device as it sits in <c>control.json</c>.
/// </summary>
/// <param name="Token">
/// Encrypted, never in the clear. On a display PC with autologon the file is readable by anyone
/// who sits at the keyboard for a minute or copies the profile - and the same argument holds for
/// the DM's machine (Part 4). What protects it is <see cref="ISecretStore"/>; that it is DPAPI
/// underneath is not this file's business.
/// </param>
/// <param name="Role">
/// What the token is good for. A display token presented at the control endpoint is refused, and
/// the other way round - kept here, in our own file, rather than parsed out of the token
/// (Part 4).
/// </param>
public sealed record KnownDevice(
    Guid DeviceId,
    string Name,
    PairingRole Role,
    string Token,
    DateTimeOffset PairedAt);

/// <summary>
/// <c>display.json</c> - what a display PC knows about itself, at a fixed location that cannot
/// be changed (Part 6, Part 9).
/// <para>
/// It carries NO screen states. All five wishes belong to the control and live in control.json;
/// a state set locally applies to the running session only. A field that is authoritative in one
/// direction and must not be read in the other eventually gets read (Part 3).
/// </para>
/// <para>
/// Plain setters rather than <c>init</c>, for the reason given at
/// <see cref="ControlConfiguration"/> - and here the cost of getting it wrong is the highest in
/// the system: a missing <c>deviceId</c> would read as <c>Guid.Empty</c>, and every display PC in
/// the house would carry the same identity.
/// </para>
/// </summary>
public sealed record DisplayConfiguration : IConfigurationDocument
{
    /// <inheritdoc/>
    public int SchemaVersion { get; set; } = ConfigurationSchema.Version;

    /// <summary>
    /// Created on the first start and kept. Losing it means losing the identity - which is
    /// exactly the price named in <see cref="ConfigurationOutcome.Replaced"/>, and the reason
    /// "reassign device" exists in the control (Part 7).
    /// </summary>
    public Guid DeviceId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The display name of this device. Null until somebody sets one, which the installer may do
    /// through <c>NAME=</c> - by filling a gap, never by writing over what is there (Part 9).
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// A host entered by hand or supplied by the installer through <c>HOST=</c>.
    /// <para>
    /// A PREFERRED address, not an exclusive one: discovery stays active even when this is set,
    /// because the address changes when the Surface moves between Wi-Fi and its dock (Part 4).
    /// </para>
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// The control this device is paired with, or null while it is unpaired. Set when pairing is
    /// allowed and cleared only by "reset pairing" at the device (Part 4, Part 6).
    /// </summary>
    public Guid? ControlId { get; set; }

    /// <summary>
    /// The device token, encrypted through <see cref="ISecretStore"/> - never in the clear, for
    /// the same reason as on the control side: this machine runs with autologon in a living room
    /// (Part 4, Part 6).
    /// <para>
    /// It travels with <see cref="ControlId"/>: a token without the control it belongs to would
    /// be offered to the first hub that answers.
    /// </para>
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Every display parameter, per screen. It is here because Part 6 requires every setting to
    /// be reachable at the device as well - and a device that has never seen a control has to be
    /// fully settable at the table and keep its settings across restarts. Were it to lose them at
    /// the first <c>ConfigUpdate</c>, local operation would be a sham (Part 4).
    /// <para>
    /// Written with EVERY value in full, never as a delta: on the wire a null means "unchanged",
    /// in this file it would mean nothing at all, and Part 6 asks that a reader can see what is
    /// settable without collecting the defaults from somewhere else.
    /// </para>
    /// </summary>
    public IReadOnlyList<ScreenPreferences> Screens { get; set; } = [];

    /// <summary>
    /// What concerns the process rather than one of its windows - written in full for the same
    /// reason.
    /// </summary>
    public DeviceSettings Device { get; set; } = new(LogLevel.Information, LogLevel.Warning, KeepAwake: true);
}

/// <summary>The stored parameters of one screen, keyed by the identifier the device derives.</summary>
public sealed record ScreenPreferences(string ScreenId, ScreenSettings Settings);

/// <summary>
/// The source-generated context for the configuration files. Same reasoning as the protocol:
/// no type resolution at run time (Part 4).
/// <para>
/// Unlike the protocol context this one writes nulls out. On the wire a null is waste; in a
/// configuration file it is the documentation: the application writes EVERY value in full on
/// the first start, so whoever opens the file can see what can be set instead of having to
/// collect the defaults from somewhere else (Part 6, Part 9). Measured on a real first run -
/// with nulls suppressed, display.json came out as two lines and told a reader nothing.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ControlConfiguration))]
[JsonSerializable(typeof(DisplayConfiguration))]
public sealed partial class ConfigurationJsonContext : JsonSerializerContext;
