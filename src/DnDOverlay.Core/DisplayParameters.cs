using Microsoft.Extensions.Logging;

namespace DnDOverlay.Core;

/// <summary>Where a display parameter applies: the whole device - its process - or one of its screens (Part 6).</summary>
public enum ParameterScope
{
    Device,
    Screen,
}

/// <summary>How a parameter is set: a number in a range, on or off, one of a few.</summary>
public enum ParameterKind
{
    Number,
    Toggle,
    Choice,
}

/// <summary>
/// One display parameter as a surface shows it (Part 6: identifier, type, range, default, scope).
/// The default is not written here; it is read from the type that already holds it -
/// <see cref="ScreenContext.Default"/> and <see cref="DeviceSettings.Defaults"/> - so it cannot drift.
/// </summary>
/// <param name="Key">The property it stands for in <see cref="ScreenSettings"/> or <see cref="DeviceSettings"/>.</param>
/// <param name="Unit">Written after a number - what the number counts.</param>
/// <param name="Decimals">How many places a number is shown and accepted with.</param>
/// <param name="Choices">The values of a choice, as they are written in the files; the first of a nullable one may be <see cref="DisplayParameters.Automatic"/>.</param>
public sealed record DisplayParameter(
    string Key,
    string Label,
    ParameterScope Scope,
    ParameterKind Kind,
    string? Unit = null,
    double Min = 0,
    double Max = 0,
    int Decimals = 0,
    IReadOnlyList<string>? Choices = null);

/// <summary>
/// The shared description of the display parameters (Part 6): the window <i>Devices</i> renders its
/// two bands out of it, and the device's tray window will too (M6) - a new parameter appears in both
/// of necessity, and which band it falls into is decided here by its scope, never by hand.
/// <para>
/// <b>Values cross as <c>double</c>, <c>bool</c> or <c>string</c></b> - number, toggle, choice - so a
/// surface needs no knowledge of the record types. Some numbers are shown in a unit other than the
/// one they are stored in: the smallest picture is kept as a fraction of the screen height and shown
/// in DIP, which is what Part 6 calls it.
/// </para>
/// <para>
/// <b>"Follows the default" means "equals the default"</b> (DM, 21.10.2026): a screen keeps only what
/// differs from the default (<see cref="ScreenSettings.Opinion"/>), and resetting sends the default
/// value, which then drops out of the files by itself.
/// </para>
/// </summary>
public static class DisplayParameters
{
    /// <summary>The choice that leaves a nullable value to the device - the diagnostic bar opposite the parking edge.</summary>
    public const string Automatic = "Automatic";

    private static readonly string[] Edges = [.. Enum.GetNames<ParkEdge>()];

    private static readonly string[] Levels =
        [nameof(LogLevel.Trace), nameof(LogLevel.Debug), nameof(LogLevel.Information), nameof(LogLevel.Warning), nameof(LogLevel.Error)];

    /// <summary>Every parameter, screen ones first, in the order a band lists them.</summary>
    public static IReadOnlyList<DisplayParameter> All { get; } =
    [
        new(nameof(ScreenSettings.Presentation), "Shown as", ParameterScope.Screen, ParameterKind.Choice, Choices: Enum.GetNames<ScreenPresentation>()),
        new(nameof(ScreenSettings.ScaleOnLoad), "Arrival size", ParameterScope.Screen, ParameterKind.Number, "of the screen height", 0.05, 1, 2),
        new(nameof(ScreenSettings.MaxWidthOnLoad), "Arrival width at most", ParameterScope.Screen, ParameterKind.Number, "of the screen width", 0.1, 1, 2),
        new(nameof(ScreenSettings.Placement), "New pictures arrive", ParameterScope.Screen, ParameterKind.Choice, Choices: Enum.GetNames<PlacementMode>()),
        new(nameof(ScreenSettings.DefaultRotationDeg), "New pictures face", ParameterScope.Screen, ParameterKind.Number, "°", 0, 359),
        new(nameof(ScreenSettings.ParkEdge), "Parked pictures line up at", ParameterScope.Screen, ParameterKind.Choice, Choices: Edges),
        new(nameof(ScreenSettings.MinScale), "Smallest picture", ParameterScope.Screen, ParameterKind.Number, "DIP on the shorter edge", 20, 600),
        new(nameof(ScreenSettings.MaxScale), "Largest picture", ParameterScope.Screen, ParameterKind.Number, "× the screen height", 1, 50, 1),
        new(nameof(ScreenSettings.MinVisiblePixels), "Always visible of a picture", ParameterScope.Screen, ParameterKind.Number, "DIP", 16, 400),
        new(nameof(ScreenSettings.ImageTextSize), "Name size", ParameterScope.Screen, ParameterKind.Number, "DIP", 8, 72),
        new(nameof(ScreenSettings.RotationDeadZoneDeg), "Turning starts after", ParameterScope.Screen, ParameterKind.Number, "°", 0, 45),
        new(nameof(ScreenSettings.RotationSnapToleranceDeg), "Snaps to right angles within", ParameterScope.Screen, ParameterKind.Number, "° (0 = off)", 0, 20),
        new(nameof(ScreenSettings.ArrivalHighlightSeconds), "Arrival highlight", ParameterScope.Screen, ParameterKind.Number, "s", 0, 5, 1),
        new(nameof(ScreenSettings.Inertia), "Pictures glide on after a push", ParameterScope.Screen, ParameterKind.Toggle),
        new(nameof(ScreenSettings.ScrollUpZoomsIn), "Scrolling up enlarges", ParameterScope.Screen, ParameterKind.Toggle),
        new(nameof(ScreenSettings.DiagnosticEdge), "Diagnostic bar at", ParameterScope.Screen, ParameterKind.Choice, Choices: [Automatic, .. Edges]),

        new(nameof(DeviceSettings.KeepAwake), "Keep the screens awake", ParameterScope.Device, ParameterKind.Toggle),
        new(nameof(DeviceSettings.RescueAfterSeconds), "Rescue mark after", ParameterScope.Device, ParameterKind.Number, "s without a control", 10, 3600),
        new(nameof(DeviceSettings.ForeignWindows), "Windows of other programs", ParameterScope.Device, ParameterKind.Choice, Choices: Enum.GetNames<ForeignWindowsMode>()),
        new(nameof(DeviceSettings.Level), "Log level", ParameterScope.Device, ParameterKind.Choice, Choices: Levels),
        new(nameof(DeviceSettings.ForwardAtLeast), "Sent to the control from", ParameterScope.Device, ParameterKind.Choice, Choices: Levels),
    ];

    /// <summary>The parameters of one band.</summary>
    public static IEnumerable<DisplayParameter> For(ParameterScope scope) => All.Where(parameter => parameter.Scope == scope);

    /// <summary>The value a screen has now, in the unit the parameter is shown in.</summary>
    public static object Value(DisplayParameter parameter, ScreenContext context)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(context);

        return parameter.Key switch
        {
            nameof(ScreenSettings.Presentation) => context.Presentation.ToString(),
            nameof(ScreenSettings.ScaleOnLoad) => context.ScaleOnLoad,
            nameof(ScreenSettings.MaxWidthOnLoad) => context.MaxWidthOnLoad,
            nameof(ScreenSettings.Placement) => context.Placement.ToString(),
            nameof(ScreenSettings.DefaultRotationDeg) => (double)context.DefaultRotationDeg,
            nameof(ScreenSettings.ParkEdge) => context.ParkEdge.ToString(),
            nameof(ScreenSettings.MinScale) => Math.Round(context.MinScale * context.HeightInDip),
            nameof(ScreenSettings.MaxScale) => context.MaxScale,
            nameof(ScreenSettings.MinVisiblePixels) => context.MinVisiblePixels,
            nameof(ScreenSettings.ImageTextSize) => context.ImageTextSize,
            nameof(ScreenSettings.RotationDeadZoneDeg) => context.RotationDeadZoneDeg,
            nameof(ScreenSettings.RotationSnapToleranceDeg) => context.RotationSnapToleranceDeg,
            nameof(ScreenSettings.ArrivalHighlightSeconds) => context.ArrivalHighlightSeconds,
            nameof(ScreenSettings.Inertia) => context.Inertia,
            nameof(ScreenSettings.ScrollUpZoomsIn) => context.ScrollUpZoomsIn,
            nameof(ScreenSettings.DiagnosticEdge) => context.DiagnosticEdge?.ToString() ?? Automatic,
            _ => throw Unknown(parameter),
        };
    }

    /// <summary>The delta that sets one screen parameter to <paramref name="value"/> - and nothing else.</summary>
    public static ScreenSettings Set(DisplayParameter parameter, ScreenContext context, object value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(value);

        return parameter.Key switch
        {
            nameof(ScreenSettings.Presentation) => new ScreenSettings(Presentation: Enum.Parse<ScreenPresentation>((string)value)),
            nameof(ScreenSettings.ScaleOnLoad) => new ScreenSettings(ScaleOnLoad: Number(value)),
            nameof(ScreenSettings.MaxWidthOnLoad) => new ScreenSettings(MaxWidthOnLoad: Number(value)),
            nameof(ScreenSettings.Placement) => new ScreenSettings(Placement: Enum.Parse<PlacementMode>((string)value)),
            nameof(ScreenSettings.DefaultRotationDeg) => new ScreenSettings(DefaultRotationDeg: (int)Math.Round(Number(value))),
            nameof(ScreenSettings.ParkEdge) => new ScreenSettings(ParkEdge: Enum.Parse<ParkEdge>((string)value)),
            nameof(ScreenSettings.MinScale) => new ScreenSettings(MinScale: context.HeightInDip <= 0 ? 0.05 : Number(value) / context.HeightInDip),
            nameof(ScreenSettings.MaxScale) => new ScreenSettings(MaxScale: Number(value)),
            nameof(ScreenSettings.MinVisiblePixels) => new ScreenSettings(MinVisiblePixels: Number(value)),
            nameof(ScreenSettings.ImageTextSize) => new ScreenSettings(ImageTextSize: Number(value)),
            nameof(ScreenSettings.RotationDeadZoneDeg) => new ScreenSettings(RotationDeadZoneDeg: Number(value)),
            nameof(ScreenSettings.RotationSnapToleranceDeg) => new ScreenSettings(RotationSnapToleranceDeg: Number(value)),
            nameof(ScreenSettings.ArrivalHighlightSeconds) => new ScreenSettings(ArrivalHighlightSeconds: Number(value)),
            nameof(ScreenSettings.Inertia) => new ScreenSettings(Inertia: (bool)value),
            nameof(ScreenSettings.ScrollUpZoomsIn) => new ScreenSettings(ScrollUpZoomsIn: (bool)value),

            // "Automatic" cannot travel: null means "unchanged" in a delta. Choosing it is no change.
            nameof(ScreenSettings.DiagnosticEdge) => (string)value == Automatic
                ? ScreenSettings.None
                : new ScreenSettings(DiagnosticEdge: Enum.Parse<ParkEdge>((string)value)),
            _ => throw Unknown(parameter),
        };
    }

    /// <summary>Whether a screen parameter is at its default - not part of what the screen keeps (<see cref="ScreenSettings.Opinion"/>).</summary>
    public static bool FollowsDefault(DisplayParameter parameter, ScreenContext context)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(context);

        return Equals(Value(parameter, context), Value(parameter, ScreenContext.Default(context.Size, context.Dpi)));
    }

    /// <summary>
    /// Whether the parameter can be put back from here. One cannot: the diagnostic bar's default is
    /// "automatic", which a delta has no way to say - null means "unchanged" (DM, 21.10.2026, the model kept).
    /// </summary>
    public static bool CanReset(DisplayParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return parameter.Key != nameof(ScreenSettings.DiagnosticEdge);
    }

    /// <summary>The delta that puts a screen parameter back to its default value.</summary>
    public static ScreenSettings Reset(DisplayParameter parameter, ScreenContext context) =>
        Set(parameter, context, Value(parameter, ScreenContext.Default(context.Size, context.Dpi)));

    /// <summary>The value a device has now.</summary>
    public static object Value(DisplayParameter parameter, DeviceSettings effective)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(effective);

        var known = DeviceSettings.Merge(DeviceSettings.Defaults, effective);

        return parameter.Key switch
        {
            nameof(DeviceSettings.KeepAwake) => known.KeepAwake ?? true,
            nameof(DeviceSettings.RescueAfterSeconds) => known.RescueAfter.TotalSeconds,
            nameof(DeviceSettings.ForeignWindows) => (known.ForeignWindows ?? ForeignWindowsMode.ReportAndControl).ToString(),
            nameof(DeviceSettings.Level) => (known.Level ?? LogLevel.Information).ToString(),
            nameof(DeviceSettings.ForwardAtLeast) => (known.ForwardAtLeast ?? LogLevel.Warning).ToString(),
            _ => throw Unknown(parameter),
        };
    }

    /// <summary>The delta that sets one device parameter.</summary>
    public static DeviceSettings Set(DisplayParameter parameter, object value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(value);

        return parameter.Key switch
        {
            nameof(DeviceSettings.KeepAwake) => new DeviceSettings(KeepAwake: (bool)value),
            nameof(DeviceSettings.RescueAfterSeconds) => new DeviceSettings(RescueAfterSeconds: (int)Math.Round(Number(value))),
            nameof(DeviceSettings.ForeignWindows) => new DeviceSettings(ForeignWindows: Enum.Parse<ForeignWindowsMode>((string)value)),
            nameof(DeviceSettings.Level) => new DeviceSettings(Level: Enum.Parse<LogLevel>((string)value)),
            nameof(DeviceSettings.ForwardAtLeast) => new DeviceSettings(ForwardAtLeast: Enum.Parse<LogLevel>((string)value)),
            _ => throw Unknown(parameter),
        };
    }

    /// <summary>Whether a device parameter is at its default.</summary>
    public static bool FollowsDefault(DisplayParameter parameter, DeviceSettings effective) =>
        Equals(Value(parameter, effective), Value(parameter, DeviceSettings.Defaults));

    /// <summary>The delta that puts a device parameter back to its default value.</summary>
    public static DeviceSettings Reset(DisplayParameter parameter) =>
        Set(parameter, Value(parameter, DeviceSettings.Defaults));

    private static double Number(object value) => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

    private static ArgumentException Unknown(DisplayParameter parameter) =>
        new($"No display parameter is called {parameter.Key} in its scope.", nameof(parameter));
}
