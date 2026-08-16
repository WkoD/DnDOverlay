using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DnDOverlay.Rendering.Windows;

/// <summary>What to draw as a caption, once the cascade has had its say.</summary>
/// <param name="Text">
/// The text to draw, already wrapped and if need be shortened. Empty means <b>draw nothing</b> -
/// the last step of the cascade, and a real outcome rather than a failure.
/// </param>
public sealed record Caption(string Text, double Width, double Height)
{
    /// <summary>Nothing survives the cascade at this size.</summary>
    public static Caption None { get; } = new(string.Empty, 0, 0);

    /// <summary>Whether there is anything to draw at all.</summary>
    public bool IsVisible => Text.Length > 0;
}

/// <summary>
/// Fitting a name into the bottom of its own picture.
/// <para>
/// The caption lies <b>inside</b> the image, so an item never reaches past its own rectangle -
/// which is what made <c>captionHeight</c> disappear from placement and what stops the Java bug
/// (<c>37e946c</c>) from recurring: there is no extra height left for anybody to forget
/// (<c>checks/M1.md</c>).
/// </para>
/// <para>
/// Measured <b>in DIP on the screen</b> rather than in normalised scene coordinates, because the
/// text does not scale with the picture. That is also why this lives here and not in
/// <c>Core</c>: measuring text is the one part that genuinely needs the platform.
/// </para>
/// </summary>
public static class CaptionLayout
{
    /// <summary>
    /// Part 6's default, and it is a starting point rather than a finding (Guide <c>G6</c>): about
    /// one and a half times standard Windows text, readable at arm's length across a table.
    /// <para>
    /// <b>Deliberately not staggered by DPI</b>, although the DPI of each screen is known. A DIP is
    /// a ninety-sixth of an inch, so a default in DIP is already physically the same size on a
    /// 96-DPI television and a 192-DPI Surface, and it follows the per-monitor scaling the user
    /// chose. Staggering it would apply the same correction twice. What it must remain is
    /// SETTABLE - because of the viewing distance, which the DPI knows nothing about: the table is
    /// an arm away, the projector three metres, the Surface half a metre.
    /// </para>
    /// </summary>
    public const double DefaultTextSize = 18;

    /// <summary>
    /// The most of the picture's height the caption may take. A portrait is there to be recognised;
    /// half was considered and dropped, because the cascade runs straight at whatever this is.
    /// </summary>
    public const double MaxHeightFraction = 1d / 3d;

    /// <summary>
    /// Fewer characters than this and the caption is dropped instead of shortened. "A handful",
    /// taken literally: <c>H…</c> covers a third of the figure and says nothing at all - worse than
    /// no caption (<c>checks/M1.md</c>).
    /// </summary>
    public const int MinimumCharacters = 5;

    private static readonly Typeface Face = new("Segoe UI");

    /// <summary>
    /// Wrap, shorten, drop - in that order, and each step only once the one before it stops
    /// carrying.
    /// </summary>
    /// <param name="widthInDip">The rendered width of the picture, which is the width to wrap into.</param>
    /// <param name="heightInDip">The rendered height, of which at most a third may be used.</param>
    public static Caption Fit(string? name, double widthInDip, double heightInDip, double textSize = DefaultTextSize)
    {
        if (string.IsNullOrWhiteSpace(name) || widthInDip <= 0 || heightInDip <= 0 || textSize <= 0)
        {
            return Caption.None;
        }

        var ceiling = heightInDip * MaxHeightFraction;

        // Step one: wrapped, whole. The common case by far - a name that fits is not shortened.
        var whole = Measure(name, widthInDip, textSize);

        if (whole.Height <= ceiling)
        {
            return new Caption(name, whole.Width, whole.Height);
        }

        // Step two: shorten with an ellipsis until it fits. Walked down from the full length rather
        // than guessed at, because how many characters fit depends on WHICH characters they are -
        // "WM" and "il" are not the same width, and a guess would be wrong per name.
        for (var length = name.Length - 1; length >= MinimumCharacters; length--)
        {
            var candidate = name[..length].TrimEnd() + "…";
            var measured = Measure(candidate, widthInDip, textSize);

            if (measured.Height <= ceiling)
            {
                return new Caption(candidate, measured.Width, measured.Height);
            }
        }

        // Step three: nothing that would still say something fits.
        return Caption.None;
    }

    private static (double Width, double Height) Measure(string text, double widthInDip, double textSize)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Face,
            textSize,
            Brushes.White,
            pixelsPerDip: 1)
        {
            MaxTextWidth = widthInDip,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.None,
        };

        return (formatted.Width, formatted.Height);
    }
}
