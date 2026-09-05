using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DnDOverlay.Control;

/// <summary>
/// The head of a tile: <b>always exactly one line</b>, in three fixed fields - name and resolution
/// on the left, the reserved field on the right (Part 7).
/// <para>
/// <b>The order of shortening is laid down rather than left to the layout</b>, because a tile that
/// rearranged itself under pressure would be a different tile every time the window is dragged
/// (Prüfschritt 32f): the reserved field never gives way, then the RESOLUTION goes entirely, and
/// only then does the name shorten with an ellipsis. <b>The name never disappears</b> - without it
/// nobody knows which tile they are looking at.
/// </para>
/// <para>
/// <b>The right-hand field is reserved and empty in M4.</b> It carries two things from M5 on: the
/// reasons mark and the battery of a device running on one - Part 7 says twice that the battery
/// stands "on the tile" and its own tile diagram has no place for it, which is a contradiction this
/// milestone found and left standing (checks/M4.md). Reserving the room here is what keeps the
/// order of shortening from being built a second time when they arrive.
/// </para>
/// </summary>
internal sealed class TileHead : Panel
{
    /// <summary>Room for a mark and a battery, in DIP. Nothing is drawn in it until M5a.</summary>
    private const double Reserved = 84;

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
        var left = Math.Max(0, finalSize.Width - Reserved);

        // The resolution goes first, and it goes ENTIRELY: half a resolution is a wrong number,
        // while half a name is still a name (Part 7).
        var resolution = _resolution.DesiredSize.Width;
        var room = left - resolution - Gap;

        if (room < _name.DesiredSize.Width && room < MinimumName)
        {
            _resolution.Arrange(new Rect(0, 0, 0, 0));
            _name.Arrange(new Rect(0, 0, Math.Max(0, left), finalSize.Height));

            return finalSize;
        }

        var forName = Math.Max(0, Math.Min(_name.DesiredSize.Width, left - resolution - Gap));

        _name.Arrange(new Rect(0, 0, forName, finalSize.Height));
        _resolution.Arrange(new Rect(forName + Gap, 0, Math.Max(0, resolution), finalSize.Height));

        return finalSize;
    }

    /// <summary>
    /// How little of the name is still worth showing beside the resolution. Below it the
    /// resolution goes instead - "TISCH-PC//D…" tells nobody which screen this is, and the
    /// resolution can be read in the devices window.
    /// </summary>
    private static double MinimumName => 90;

    private double Wanted() => _name.DesiredSize.Width + Gap + _resolution.DesiredSize.Width + Reserved;
}
