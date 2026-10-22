using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DnDOverlay.Rendering.Windows;

/// <summary>
/// How a free text looks (M5b9): white words on a half-transparent dark plate with rounded corners -
/// readable over a bright map and over a dark one alike, from the far side of a table.
/// <para>
/// <b>One drawing, every surface.</b> The table, the thumbnail and the hand carrying it draw the same
/// image, and the control measures the text's shape from it before sending - so a caption is exactly
/// as wide as its words wherever it shows, and it scales like a picture because it IS one: a vector
/// drawing, sharp at any size, with no decode and no asset.
/// </para>
/// </summary>
public static class TextCard
{
    /// <summary>The size the drawing is made at; it is stretched to the item's place like any picture.</summary>
    private const double EmSize = 64;

    private const double Padding = EmSize * 0.35;

    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush Plate = Freeze(new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x14)));
    private static readonly Brush Ink = Freeze(Brushes.White);

    /// <summary>Kept per text: a handful are on a table, and each pass would otherwise lay the words out again.</summary>
    private static readonly Dictionary<string, DrawingImage> Drawn = new(StringComparer.Ordinal);

    /// <summary>The text's shape - width over height of its drawing, plate included.</summary>
    public static double AspectRatio(string text)
    {
        var drawing = Draw(text);

        return drawing.Height > 0 ? drawing.Width / drawing.Height : 1;
    }

    /// <summary>The drawing of a text - frozen, so any thread may hand it to WPF.</summary>
    public static DrawingImage Draw(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (Drawn)
        {
            if (Drawn.TryGetValue(text, out var known))
            {
                return known;
            }

            // A table holds a handful; the cache only has to spare relaying them out every pass.
            if (Drawn.Count > 256)
            {
                Drawn.Clear();
            }

            var words = new FormattedText(
                text.Length == 0 ? " " : text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Face,
                EmSize,
                Ink,
                pixelsPerDip: 1)
            {
                TextAlignment = TextAlignment.Center,
            };

            var width = words.WidthIncludingTrailingWhitespace + (2 * Padding);
            var height = words.Height + (2 * Padding);
            var group = new DrawingGroup();

            using (var pen = group.Open())
            {
                pen.DrawRoundedRectangle(Plate, null, new Rect(0, 0, width, height), Padding, Padding);
                pen.DrawText(words, new Point(width / 2, Padding));
            }

            group.Freeze();

            var image = new DrawingImage(group);

            image.Freeze();
            Drawn[text] = image;

            return image;
        }
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();

        return brush;
    }
}
