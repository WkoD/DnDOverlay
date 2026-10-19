using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BatteryInfo = DnDOverlay.Core.BatteryInfo;

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
/// <b>Since M5a the right edge holds the battery and the reasons mark</b> (DM, 19.10.2026: both on
/// the right). They are things one reads at a glance or presses, so they get a fixed place, and the
/// name and resolution run up to them and are cut off there. They take only the room they need:
/// a device without a battery and a screen without reasons leave the whole width to the text, and
/// nothing of the earlier reserved field comes back.
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

    private readonly BatteryGauge _battery = new() { Margin = new Thickness(Gap, 0, 0, 0) };

    private readonly TextBlock _mark = new()
    {
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(Gap, 0, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>The name and resolution, cut off where the right-hand things begin.</summary>
    private readonly Canvas _text = new() { ClipToBounds = true };

    internal TileHead()
    {
        // The whole strip answers, not only the words on it. A panel without a background is
        // invisible to the hit test between its children, so the screen menu and the drag could
        // only be started where there happened to be text (hand-run of M4, 25v).
        Background = Brushes.Transparent;

        // What does not fit is cut off here rather than argued about above.
        ClipToBounds = true;

        _text.Children.Add(_name);
        _text.Children.Add(_resolution);

        Children.Add(_text);
        Children.Add(_battery);
        Children.Add(_mark);
    }

    /// <summary>
    /// The colour of the head: grey when nothing of ours lies on the table, ordinary otherwise
    /// (Part 7). It answers "do we show anything there?" across four tiles without reading, which is
    /// why frozen and diagnostic keep the ordinary head - they still show pictures.
    /// </summary>
    internal bool Inactive
    {
        set => Background = value ? HeadGrey : Brushes.Transparent;
    }

    private static readonly Brush HeadGrey = Frozen(Color.FromRgb(0xC4, 0xC4, 0xC4));

    /// <summary>The battery of this screen's device, or nothing.</summary>
    internal void Battery(BatteryInfo? battery)
    {
        _battery.Show(battery);
        InvalidateMeasure();
    }

    /// <summary>The reasons mark - "⚠ 2 reasons ▾" - or an empty field when there are none.</summary>
    internal void Reasons(string? mark)
    {
        _mark.Text = mark is null ? string.Empty : mark + " ▾";
        _mark.Visibility = mark is null ? Visibility.Collapsed : Visibility.Visible;
        InvalidateMeasure();
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
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
        _battery.Measure(endless);
        _mark.Measure(endless);

        var height = Math.Max(
            Math.Max(_name.DesiredSize.Height, _resolution.DesiredSize.Height),
            Math.Max(_battery.DesiredSize.Height, _mark.DesiredSize.Height));

        _text.Measure(new Size(double.PositiveInfinity, height));

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
        // The right-hand things first, at their own width against the right edge; the text gets
        // what is left and is clipped there.
        var mark = _mark.Visibility == Visibility.Visible ? _mark.DesiredSize.Width : 0;
        var battery = _battery.Visibility == Visibility.Visible ? _battery.DesiredSize.Width : 0;
        var right = finalSize.Width - mark;

        _mark.Arrange(new Rect(right, 0, mark, finalSize.Height));

        right -= battery;
        _battery.Arrange(new Rect(right, 0, battery, finalSize.Height));

        _text.Arrange(new Rect(0, 0, Math.Max(0, right), finalSize.Height));

        var name = _name.DesiredSize.Width;

        Canvas.SetLeft(_name, 0);
        Canvas.SetLeft(_resolution, name + Gap);
        _name.Height = finalSize.Height;
        _resolution.Height = finalSize.Height;

        return finalSize;
    }

    /// <summary>What the head would like if nobody says how much room there is: everything, side by side.</summary>
    private double Wanted() =>
        _name.DesiredSize.Width + Gap + _resolution.DesiredSize.Width
        + (_battery.Visibility == Visibility.Visible ? _battery.DesiredSize.Width : 0)
        + (_mark.Visibility == Visibility.Visible ? _mark.DesiredSize.Width : 0);
}
