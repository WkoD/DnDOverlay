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
    ParkEdge ParkEdge)
{
    /// <summary>The screen height in DIP - the unit <see cref="MinVisiblePixels"/> is given in.</summary>
    public double HeightInDip => Dpi <= 0 ? Size.Height : Size.Height * 96d / Dpi;

    /// <summary>The screen width in DIP.</summary>
    public double WidthInDip => Dpi <= 0 ? Size.Width : Size.Width * 96d / Dpi;

    /// <summary><see cref="MinVisiblePixels"/> expressed in the normalised unit the scene uses.</summary>
    public double MinVisibleNormalised =>
        HeightInDip <= 0 ? 0 : MinVisiblePixels / HeightInDip;

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
            ScaleOnLoad: 0.5,
            MaxWidthOnLoad: 0.9,
            Placement: PlacementMode.Flow,
            DefaultRotationDeg: 0,
            ParkEdge: ParkEdge.Right);
    }
}
