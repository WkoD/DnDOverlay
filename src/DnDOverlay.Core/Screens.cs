using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core;

/// <summary>The five states a screen can be in - all of them the DM's wish (Part 3).</summary>
public enum ScreenState
{
    /// <summary>An overlay lies on the screen and gestures work.</summary>
    Enabled = 0,

    /// <summary>Like <see cref="Enabled"/>, plus the diagnostic bar over the images.</summary>
    Diagnostic = 1,

    /// <summary>Like <see cref="Enabled"/>, but the screen accepts no gestures - "frozen".</summary>
    Disabled = 2,

    /// <summary>A black curtain over everything, no input.</summary>
    Blackout = 3,

    /// <summary>The screen exists in Windows and we deliberately put no window on it.</summary>
    Inactive = 4,
}

/// <summary>Where parked images line up. Configurable per screen, because a table has no natural top edge.</summary>
public enum ParkEdge
{
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3,
}

/// <summary>How a new image finds its place when the DM did not aim (Part 3).</summary>
public enum PlacementMode
{
    /// <summary>Side by side from the top left, wrapping - the mode that counts when images come quickly.</summary>
    Flow = 0,

    /// <summary>Stacked with a growing offset from the centre.</summary>
    Cascade = 1,
}

/// <summary>
/// What the display reports about one screen - and nothing else. The state is deliberately NOT
/// in here: it is the DM's wish, it is born in the control and it only ever travels outwards.
/// A field that holds in one direction and must not be read in the other gets read eventually
/// (Part 3, Part 4).
/// </summary>
/// <param name="ScreenId">Internal, never shown in any surface.</param>
/// <param name="Label">The effective display name: <paramref name="CustomName"/>, else "device//screen".</param>
/// <param name="CustomName">Flows upwards too, because it may be given at the device (Part 6).</param>
/// <param name="Size">Physical pixels.</param>
/// <param name="Dpi">Effective DPI of this monitor; 96 means unscaled.</param>
public sealed record ScreenInfo(
    ScreenId ScreenId,
    string Label,
    string? CustomName,
    PixelSize Size,
    double Dpi,
    bool IsPrimary);

/// <summary>
/// Why a screen is not being played on at the moment, although the DM wishes it were.
/// <para>
/// All of these are TRANSIENT FINDINGS and none of them is written into
/// <see cref="ScreenState"/>. A finding that overwrote the wish would have to restore it
/// afterwards - so remember what held before, and keep that memory consistent across crashes,
/// restarts and simultaneous changes. That is exactly where such models come apart: a screen is
/// unplugged, somebody changes the wish meanwhile, and the wrong value wins when it comes back.
/// Leaving the wish untouched means there is NOTHING to restore (Part 3).
/// </para>
/// </summary>
public enum SuppressReason
{
    /// <summary>Gone from the Windows inventory, or its device is not connected.</summary>
    Unavailable = 0,

    /// <summary>
    /// The control's own window lies on it, so an always-on-top overlay would cover the DM's
    /// stage. Only ever set for devices on the SAME machine, told apart by the loopback
    /// interface - a foreign table with a coincidentally equal ScreenId is none of our business
    /// (Part 2, Part 3).
    /// </summary>
    ControlWindow = 1,

    /// <summary>
    /// Hidden at the device itself with the rescue mark, because no control was reachable. Set
    /// and cleared on the display side; any arriving <c>ConfigUpdate</c> clears it (Part 6).
    /// Reaches the wire in M5a, when the mark itself is built.
    /// </summary>
    HiddenAtDevice = 2,
}

/// <summary>
/// What the control knows about one screen: what the device reported, what the DM wishes, and
/// what is getting in the way right now. The two halves are only put together here - the wish is
/// never part of <see cref="ScreenInfo"/>, and the finding is never part of the wish (Part 3).
/// </summary>
/// <param name="Screen">
/// The full address, and it has to be here rather than be implied. Part 3 sketches this type
/// carrying only the reported facts, inside a device record that supplies the
/// <see cref="DeviceId"/> - which works as long as it is always read through that record. Flat,
/// it would be exactly the thing Part 3 rules out everywhere else: two cloned display PCs can
/// report literally the same <see cref="ScreenId"/>, so a view identified by one alone would
/// belong to either of two tables.
/// </param>
/// <param name="Suppressed">
/// <see langword="null"/> when nothing is in the way. The scene stays fully visible and fully
/// playable regardless; what rests is the presentation.
/// </param>
public sealed record ScreenView(
    ScreenRef Screen,
    ScreenInfo Info,
    ScreenState State,
    SuppressReason? Suppressed = null);

/// <summary>
/// A DELTA over the display parameters of one screen: <see langword="null"/> means "unchanged",
/// never "cleared".
/// <para>
/// It has to be a delta because the same value has two writers - the control and the device
/// itself, where Part 6 requires every setting to be reachable as well. A full set in one
/// direction would reset the other side's change without anybody ordering it. Which is not
/// cosmetic: four of these act in the HUB rather than at the device - the two load values enter
/// the cap, <see cref="PlacementMode"/> the placement, <see cref="DefaultRotationDeg"/> every new
/// item, and <see cref="ParkEdge"/> decides where parked images line up (Part 4).
/// </para>
/// <para>
/// A record of nullable fields rather than a name/value map, for the same reason the protocol has
/// no open type resolution: what can be set is a closed list, and a map would carry
/// <c>object</c> values that no source generator can write (Part 4).
/// </para>
/// </summary>
public sealed record ScreenSettings(
    string? CustomName = null,
    double? MinVisiblePixels = null,
    double? MinScale = null,
    double? MaxScale = null,
    double? ScaleOnLoad = null,
    double? MaxWidthOnLoad = null,
    PlacementMode? Placement = null,
    int? DefaultRotationDeg = null,
    ParkEdge? ParkEdge = null,
    double? ImageTextSize = null,
    double? RotationDeadZoneDeg = null,
    double? RotationSnapToleranceDeg = null,
    double? ArrivalHighlightSeconds = null,
    bool? Inertia = null,
    bool? ScrollUpZoomsIn = null)
{
    /// <summary>Nothing changed - the answer when a diff finds no difference.</summary>
    public static readonly ScreenSettings None = new();

    /// <summary>
    /// Whether this delta says anything at all.
    /// <para>
    /// Not serialised, and that is not tidiness: a computed property has no business in a file
    /// that documents what is settable, and on the wire it would be a field both ends carry and
    /// neither reads. Measured on a real first run - display.json came out with an "isEmpty" in
    /// it.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty =>
        CustomName is null
        && MinVisiblePixels is null
        && MinScale is null
        && MaxScale is null
        && ScaleOnLoad is null
        && MaxWidthOnLoad is null
        && Placement is null
        && DefaultRotationDeg is null
        && ParkEdge is null
        && ImageTextSize is null
        && RotationDeadZoneDeg is null
        && RotationSnapToleranceDeg is null
        && ArrivalHighlightSeconds is null
        && Inertia is null
        && ScrollUpZoomsIn is null;

    /// <summary>
    /// The full effective set of a screen, for the baseline in the <c>Hello</c>. Size and DPI are
    /// deliberately absent: they are hardware facts, they come with the screen list, and a device
    /// that could set them would be able to lie about its own monitor.
    /// </summary>
    public static ScreenSettings Of(ScreenContext context, string? customName)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ScreenSettings(
            CustomName: customName,
            MinVisiblePixels: context.MinVisiblePixels,
            MinScale: context.MinScale,
            MaxScale: context.MaxScale,
            ScaleOnLoad: context.ScaleOnLoad,
            MaxWidthOnLoad: context.MaxWidthOnLoad,
            Placement: context.Placement,
            DefaultRotationDeg: context.DefaultRotationDeg,
            ParkEdge: context.ParkEdge,
            ImageTextSize: context.ImageTextSize,
            RotationDeadZoneDeg: context.RotationDeadZoneDeg,
            RotationSnapToleranceDeg: context.RotationSnapToleranceDeg,
            ArrivalHighlightSeconds: context.ArrivalHighlightSeconds,
            Inertia: context.Inertia,
            ScrollUpZoomsIn: context.ScrollUpZoomsIn);
    }

    /// <summary>Lays this delta over a context, leaving untouched whatever it does not mention.</summary>
    public ScreenContext ApplyTo(ScreenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context with
        {
            MinVisiblePixels = MinVisiblePixels ?? context.MinVisiblePixels,
            MinScale = MinScale ?? context.MinScale,
            MaxScale = MaxScale ?? context.MaxScale,
            ScaleOnLoad = ScaleOnLoad ?? context.ScaleOnLoad,
            MaxWidthOnLoad = MaxWidthOnLoad ?? context.MaxWidthOnLoad,
            Placement = Placement ?? context.Placement,
            DefaultRotationDeg = DefaultRotationDeg ?? context.DefaultRotationDeg,
            ParkEdge = ParkEdge ?? context.ParkEdge,
            ImageTextSize = ImageTextSize ?? context.ImageTextSize,
            RotationDeadZoneDeg = RotationDeadZoneDeg ?? context.RotationDeadZoneDeg,
            RotationSnapToleranceDeg = RotationSnapToleranceDeg ?? context.RotationSnapToleranceDeg,
            ArrivalHighlightSeconds = ArrivalHighlightSeconds ?? context.ArrivalHighlightSeconds,
            Inertia = Inertia ?? context.Inertia,
            ScrollUpZoomsIn = ScrollUpZoomsIn ?? context.ScrollUpZoomsIn,
        };
    }

    /// <summary>
    /// Two deltas laid on top of each other, the newer one winning per key. The hub needs this
    /// where a device changed something while an earlier change was still pending, and it belongs
    /// here beside the other three rather than at the call site.
    /// <para>
    /// <b>It lived in the hub until M3, and it was wrong there.</b> Written out positionally, it
    /// listed nine of ten fields, and <c>ImageTextSize</c> - added in M2, one milestone earlier -
    /// fell out of every merge without a sound. That is the fifth place a screen parameter has to
    /// be known, and the round-trip test now holds this one too.
    /// </para>
    /// </summary>
    public static ScreenSettings Merge(ScreenSettings older, ScreenSettings newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);

        return new ScreenSettings(
            CustomName: newer.CustomName ?? older.CustomName,
            MinVisiblePixels: newer.MinVisiblePixels ?? older.MinVisiblePixels,
            MinScale: newer.MinScale ?? older.MinScale,
            MaxScale: newer.MaxScale ?? older.MaxScale,
            ScaleOnLoad: newer.ScaleOnLoad ?? older.ScaleOnLoad,
            MaxWidthOnLoad: newer.MaxWidthOnLoad ?? older.MaxWidthOnLoad,
            Placement: newer.Placement ?? older.Placement,
            DefaultRotationDeg: newer.DefaultRotationDeg ?? older.DefaultRotationDeg,
            ParkEdge: newer.ParkEdge ?? older.ParkEdge,
            ImageTextSize: newer.ImageTextSize ?? older.ImageTextSize,
            RotationDeadZoneDeg: newer.RotationDeadZoneDeg ?? older.RotationDeadZoneDeg,
            RotationSnapToleranceDeg: newer.RotationSnapToleranceDeg ?? older.RotationSnapToleranceDeg,
            ArrivalHighlightSeconds: newer.ArrivalHighlightSeconds ?? older.ArrivalHighlightSeconds,
            Inertia: newer.Inertia ?? older.Inertia,
            ScrollUpZoomsIn: newer.ScrollUpZoomsIn ?? older.ScrollUpZoomsIn);
    }

    /// <summary>
    /// What changed between two full sets - the delta that goes on the wire. Comparing doubles
    /// exactly is right here rather than sloppy: both sides carry the same value through, so a
    /// difference means somebody set something.
    /// </summary>
    public static ScreenSettings Diff(ScreenSettings before, ScreenSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return new ScreenSettings(
            CustomName: string.Equals(before.CustomName, after.CustomName, StringComparison.Ordinal)
                ? null
                : after.CustomName,
            MinVisiblePixels: before.MinVisiblePixels == after.MinVisiblePixels ? null : after.MinVisiblePixels,
            MinScale: before.MinScale == after.MinScale ? null : after.MinScale,
            MaxScale: before.MaxScale == after.MaxScale ? null : after.MaxScale,
            ScaleOnLoad: before.ScaleOnLoad == after.ScaleOnLoad ? null : after.ScaleOnLoad,
            MaxWidthOnLoad: before.MaxWidthOnLoad == after.MaxWidthOnLoad ? null : after.MaxWidthOnLoad,
            Placement: before.Placement == after.Placement ? null : after.Placement,
            DefaultRotationDeg: before.DefaultRotationDeg == after.DefaultRotationDeg
                ? null
                : after.DefaultRotationDeg,
            ParkEdge: before.ParkEdge == after.ParkEdge ? null : after.ParkEdge,
            ImageTextSize: before.ImageTextSize == after.ImageTextSize ? null : after.ImageTextSize,
            RotationDeadZoneDeg: before.RotationDeadZoneDeg == after.RotationDeadZoneDeg
                ? null
                : after.RotationDeadZoneDeg,
            RotationSnapToleranceDeg: before.RotationSnapToleranceDeg == after.RotationSnapToleranceDeg
                ? null
                : after.RotationSnapToleranceDeg,
            ArrivalHighlightSeconds: before.ArrivalHighlightSeconds == after.ArrivalHighlightSeconds
                ? null
                : after.ArrivalHighlightSeconds,
            Inertia: before.Inertia == after.Inertia ? null : after.Inertia,
            ScrollUpZoomsIn: before.ScrollUpZoomsIn == after.ScrollUpZoomsIn
                ? null
                : after.ScrollUpZoomsIn);
    }
}

/// <summary>
/// The delta over what belongs to the DEVICE rather than to one of its screens - the parameters
/// that concern the PROCESS, not a window (Part 6).
/// <para>
/// M1b has the three that exist: what the device produces at all, what of it is worth the wire,
/// and whether it keeps its screens awake. The rest of the table - image store, foreign windows,
/// language - arrives with the milestone that builds it.
/// </para>
/// </summary>
/// <param name="Level">
/// What the device WRITES, into its ring buffer and its file alike. Raising a single display to
/// <c>Debug</c> from the far side of the house is the documented way to hunt a fault (Part 8) -
/// and the reason the message rate limit follows the level instead of being fixed (Part 4).
/// </param>
/// <param name="ForwardAtLeast">What of it goes over the wire. Warning by default.</param>
/// <param name="KeepAwake">
/// Whether the device holds its screens on while it is connected. On by default, and switchable
/// from afar because the one machine it matters on is the one nobody is sitting at (Part 6).
/// </param>
public sealed record DeviceSettings(
    LogLevel? Level = null,
    LogLevel? ForwardAtLeast = null,
    bool? KeepAwake = null)
{
    /// <summary>Whether this delta says anything at all. Not serialised - see ScreenSettings.</summary>
    [JsonIgnore]
    public bool IsEmpty => Level is null && ForwardAtLeast is null && KeepAwake is null;

    /// <summary>
    /// Two deltas laid over one another, the newer winning field by field.
    /// <para>
    /// It lives here and not at either call site because there are two of them - the baseline a
    /// <c>Hello</c> brings and a change from the control - and a field added to the record has to
    /// reach both or the one that was forgotten silently drops it (rule "exactly once").
    /// </para>
    /// </summary>
    public static DeviceSettings Merge(DeviceSettings older, DeviceSettings newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);

        return new DeviceSettings(
            newer.Level ?? older.Level,
            newer.ForwardAtLeast ?? older.ForwardAtLeast,
            newer.KeepAwake ?? older.KeepAwake);
    }
}

/// <summary>
/// How one screen stands, as the control alone may say it. Either wholly present or wholly
/// absent - unlike <see cref="ScreenSettings"/> this is NOT a delta, and it does not need to be:
/// it has one writer, so there is nothing on the other side that could be overwritten.
/// <para>
/// A display never sends this. Should one arrive from a device anyway, it is passed over and
/// logged rather than obeyed: all five wishes are born in the control and travel one way (Part 3,
/// Part 4).
/// </para>
/// </summary>
/// <param name="Suppress">
/// <see langword="null"/> means "nothing in the way" - which is also how a finding is CLEARED,
/// since the field is always complete.
/// </param>
public sealed record ScreenCommand(ScreenState State, SuppressReason? Suppress = null);

/// <summary>What one <c>ConfigUpdate</c> has to say about one screen.</summary>
public sealed record ScreenConfigUpdate(
    ScreenId Screen,
    ScreenSettings? Settings = null,
    ScreenCommand? Command = null);

/// <summary>
/// The payload of a <c>ConfigUpdate</c>, and of the baseline a <c>Hello</c> brings along.
/// <para>
/// The same mechanism runs in both directions - that is the whole design. From the device it
/// carries settings only; from the control it may carry the wish and the finding as well. And the
/// <c>Hello</c> uses the same shape with FULL values instead of a delta, because a baseline is
/// exactly what it is (Part 4).
/// </para>
/// </summary>
public sealed record ConfigUpdate(
    IReadOnlyList<ScreenConfigUpdate> Screens,
    DeviceSettings? Device = null)
{
    /// <summary>Nothing to say - the answer when a diff over both halves comes up empty.</summary>
    public static readonly ConfigUpdate None = new([]);

    [JsonIgnore]
    public bool IsEmpty => Screens.Count == 0 && (Device is null || Device.IsEmpty);

    /// <summary>
    /// Structural over the screen list, for the same reason every other list-bearing DTO is: a
    /// record compares list members by REFERENCE, so a message that went through the wire would
    /// never equal the one that was sent.
    /// </summary>
    public bool Equals(ConfigUpdate? other) =>
        other is not null && Device == other.Device && Screens.SequenceEqual(other.Screens);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Device);

        foreach (var screen in Screens)
        {
            hash.Add(screen);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Everything a computation over a scene needs, handed into the reducer and into
/// <see cref="Layout.ItemToRect"/> (Part 1, rules 2 and 9). Half hardware fact, half display
/// parameter (Part 6); kept apart there would be two things that never occur singly.
/// <para>
/// It is persisted per known screen in control.json, and that is what carries a promise which
/// could not be kept otherwise: a screen is fully playable in every state and on every finding -
/// expressly including while its device is switched OFF. Were size and DPI only ever to arrive
/// in the <c>Hello</c>, the hub could neither place nor clamp for an absent device, and
/// preparing ahead would fall away (Part 3).
/// </para>
/// </summary>
/// <param name="MinVisiblePixels">
/// In DIP, not physical pixels: this hangs on finger size, and 60 physical pixels are a few
/// millimetres on 4K. Also the width of a park slot - one number for both (Part 6).
/// </param>
/// <param name="MinScale">
/// The smallest rendered SHORTER edge, as a fraction of the screen height. Part 6 phrases it as
/// "80 DIP on the shorter edge", and that depends on the item's aspect ratio, so it cannot be a
/// plain scale factor - <see cref="Layout.ClampScale"/> does the conversion.
/// </param>
/// <param name="MaxScale">Upper bound on <c>Scale</c> itself: 10 means ten screen heights.</param>
/// <param name="ScaleOnLoad">Height of a freshly inserted image, as a fraction of the screen height.</param>
/// <param name="MaxWidthOnLoad">
/// Width cap for the same, as a fraction of the screen WIDTH. Without it a 5000×500 panorama
/// arrives three times as wide as the table (Part 3).
/// </param>
public sealed record ScreenContext(
    PixelSize Size,
    double Dpi,
    double MinVisiblePixels,
    double MinScale,
    double MaxScale,
    double ScaleOnLoad,
    double MaxWidthOnLoad,
    PlacementMode Placement,
    int DefaultRotationDeg,
    ParkEdge ParkEdge,

    /// <summary>
    /// How tall the name in a picture is drawn, in DIP on that screen (Part 6).
    /// <para>
    /// Per screen and not once for the program, because the only thing that separates the three
    /// cases is the VIEWING DISTANCE, and the DPI knows nothing about it: the table is an arm away,
    /// the projector three metres, the Surface half a metre. DIP already carries the user's chosen
    /// Windows scaling, so what is left over is exactly the correction no machine can make.
    /// </para>
    /// </summary>
    double ImageTextSize,

    /// <summary>
    /// Below this angle a two-finger gesture does not rotate at all (Part 6). Two fingers turn a
    /// picture a LITTLE every time; without the dead zone everything on the table stands crooked
    /// after an evening, and nobody meant to do it.
    /// </summary>
    double RotationDeadZoneDeg,

    /// <summary>
    /// How close to a quarter turn an angle has to end up to be pulled onto it when the finger
    /// lifts - <c>0</c> switches snapping off. Never DURING the gesture: a picture that clicks
    /// into place under the finger feels broken (Part 6).
    /// </summary>
    double RotationSnapToleranceDeg,

    /// <summary>
    /// How long a newly arrived picture lights up, in seconds - <c>0</c> switches it off, the same
    /// shape <see cref="RotationSnapToleranceDeg"/> uses. On a table holding twelve pictures a
    /// thirteenth appears somewhere in the flow order and nobody notices it; sound is not available
    /// as a channel, so the picture has to draw attention to itself (Part 6).
    /// </summary>
    double ArrivalHighlightSeconds,

    /// <summary>
    /// Whether a pushed picture glides on after the finger leaves, damped towards the edge
    /// (Part 6). Off is safe and wooden - on a table lying flat one pushes across real distances.
    /// </summary>
    bool Inertia,

    /// <summary>
    /// Wheel up makes a picture larger. Java had it the other way round, which is the one thing
    /// about a wheel that everybody has an opinion on (Part 6).
    /// </summary>
    bool ScrollUpZoomsIn)
{
    /// <summary>The screen height in DIP - the unit <see cref="MinVisiblePixels"/> is given in.</summary>
    public double HeightInDip => Dpi <= 0 ? Size.Height : Size.Height * 96d / Dpi;

    /// <summary>The screen width in DIP.</summary>
    public double WidthInDip => Dpi <= 0 ? Size.Width : Size.Width * 96d / Dpi;

    /// <summary>
    /// <see cref="MinVisiblePixels"/> expressed in the normalised unit of the VERTICAL axis.
    /// <para>
    /// There are two of these and there has to be: normalised Y is a fraction of the screen height
    /// and normalised X a fraction of its WIDTH, so the same physical length is a different number
    /// on each axis. Using one of them for both leaves 96 DIP standing at the top edge and 54 at
    /// the side of a 16:9 table - the edge clamp would be nearly half as strict sideways as it
    /// promises.
    /// </para>
    /// </summary>
    public double MinVisibleNormalisedY =>
        HeightInDip <= 0 ? 0 : MinVisiblePixels / HeightInDip;

    /// <summary><see cref="MinVisiblePixels"/> expressed in the normalised unit of the horizontal axis.</summary>
    public double MinVisibleNormalisedX =>
        WidthInDip <= 0 ? 0 : MinVisiblePixels / WidthInDip;

    /// <summary>
    /// The screen's own aspect ratio. It has to enter the width cap, because <c>Scale</c> means
    /// HEIGHT while <see cref="MaxWidthOnLoad"/> means WIDTH - without it the cap bites 1.78
    /// times too hard on 16:9 (Part 3).
    /// </summary>
    public double AspectRatio => Size.AspectRatio;

    /// <summary>
    /// The defaults from the parameter table in Part 6, for a screen of the given size and DPI.
    /// The application overrides from display.json on top of this; the values live here so the
    /// reducer never has to reach for configuration.
    /// </summary>
    public static ScreenContext Default(PixelSize size, double dpi)
    {
        var heightInDip = dpi <= 0 ? size.Height : size.Height * 96d / dpi;

        return new ScreenContext(
            Size: size,
            Dpi: dpi,
            MinVisiblePixels: 96,
            MinScale: heightInDip <= 0 ? 0.05 : 80d / heightInDip,
            MaxScale: 10,

            // 0.4, not the 0.5 of the parameter table in Part 6, and the difference is not
            // cosmetic: Scale means HEIGHT, so at 0.5 a 4:3 picture measures 0.375 x 0.5 normalised
            // and the flow grid holds exactly TWO slots. Measured at the table (hand-run of M2b,
            // step 16), which is where "flow does not flow" came from. 0.4 gives six.
            ScaleOnLoad: 0.4,
            MaxWidthOnLoad: 0.9,
            Placement: PlacementMode.Flow,
            DefaultRotationDeg: 0,
            ParkEdge: ParkEdge.Right,

            // Around one and a half times Windows' standard text: readable at arm's length on the
            // table without a small portrait running straight into the truncation (decided in
            // checks/M2.md). A starting point, and one the hand-run is meant to move.
            ImageTextSize: 18,

            // All five are proposals from the parameter table in Part 6 and stay proposals until
            // the hand-run of M3b has had fingers on them. Whichever of them turns out wrong, the
            // NUMBER is corrected afterwards and not the test (Guide G6).
            RotationDeadZoneDeg: 5,
            RotationSnapToleranceDeg: 4,

            // The first of the five the hand-run has actually moved: 2 s was the proposal, and at
            // the table it is not a flash but a state - the picture stands under a white veil for
            // two whole seconds and only then is itself. What the highlight is for is being noticed
            // among twelve others, and that is over in a fraction of a second.
            ArrivalHighlightSeconds: 0.8,
            Inertia: true,
            ScrollUpZoomsIn: true);
    }
}
