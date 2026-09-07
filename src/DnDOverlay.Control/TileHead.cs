using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DnDOverlay.Control;

/// <summary>
/// The head of a tile: <b>always exactly one line</b> - the screen's name, and the resolution
/// beside it (Part 7).
/// <para>
/// <b>Name and resolution, and no rule about which of them gives way.</b> There was one: the
/// resolution went entirely first, then the name shortened, and a field on the right was kept free.
/// At the table it produced the opposite of what it promised (Handlauf M4, dritter Lauf, 32f) - the
/// resolution slid over the name, the name blanked out, and all of it happened while there was
/// still white space to the right. Decided by the DM on the spot: <i>„Generell sollte immer die
/// ganze Breite ausgenutzt werden … keine Vorrangregel oder sonstiges, das wäre erst mit Buttons
/// oder ähnlichem nötig."</i>
/// </para>
/// <para>
/// So both stand where they stand, the strip uses its whole width, and what does not fit is simply
/// <b>cut off at the right edge</b> - the head is clipped rather than negotiated. It is the same
/// reasoning the tile itself follows when the window gets narrow: the tile keeps its size and
/// leaves the window. A cascade is worth building when something has to be CHOSEN between; two
/// pieces of text in reading order have nothing to choose.
/// </para>
/// <para>
/// <b>The reserved field on the right is gone with it.</b> It was held free for the reasons mark
/// and the battery from M5a, and holding it was what made the strip run out of room early. When
/// those arrive they are things one presses or reads at a glance, and then the head needs a real
/// layout decision rather than a number kept warm for a year (Teil 7 nachzuziehen).
/// </para>
/// </summary>
internal sealed class TileHead : Panel
{
    private const double Gap = 8;

    private readonly TextBlock _name = new()
    {
        FontWeight = FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _resolution = new()
    {
        Opacity = 0.7,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.None,
    };

    internal TileHead()
    {
        // The whole strip answers, not only the words on it. A panel without a background is
        // invisible to the hit test between its children, so the screen menu and the drag could
        // only be started where there happened to be text (hand-run of M4, 25v).
        Background = Brushes.Transparent;

        // What does not fit is cut off here rather than argued about above.
        ClipToBounds = true;

        Children.Add(_name);
        Children.Add(_resolution);
    }

    /// <summary>
    /// What the head says: the screen's name, and how much room there is on it.
    /// <para>
    /// The resolution comes from the persisted context and therefore stands even when the device is
    /// switched off - which is exactly when the DM is preparing (Part 7).
    /// </para>
    /// </summary>
    internal void Show(string label, int width, int height)
    {
        _name.Text = label;
        _resolution.Text = $"{width}×{height}";

        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var endless = new Size(double.PositiveInfinity, double.PositiveInfinity);

        _name.Measure(endless);
        _resolution.Measure(endless);

        var height = Math.Max(_name.DesiredSize.Height, _resolution.DesiredSize.Height);

        return new Size(
            double.IsInfinity(availableSize.Width) ? Wanted() : availableSize.Width,
            height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Both at their own width, in reading order, from the left edge. Neither is trimmed and
        // neither is dropped: the panel clips, so a strip too narrow for both simply shows as much
        // as it has room for - which is what the DM asked for and what a reader expects of a line
        // of text in a box that got smaller.
        var name = _name.DesiredSize.Width;

        _name.Arrange(new Rect(0, 0, name, finalSize.Height));
        _resolution.Arrange(new Rect(
            name + Gap, 0, Math.Max(0, _resolution.DesiredSize.Width), finalSize.Height));

        return finalSize;
    }

    /// <summary>What the head would like if nobody says how much room there is: both, side by side.</summary>
    private double Wanted() => _name.DesiredSize.Width + Gap + _resolution.DesiredSize.Width;
}
