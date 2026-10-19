using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BatteryInfo = DnDOverlay.Core.BatteryInfo;

namespace DnDOverlay.Control;

/// <summary>
/// A display's battery as Windows 11 draws one: a battery shape filled to its level, green, red from
/// <see cref="BatteryInfo.LowPercent"/> down, a lightning sign on the cable, and the percentage
/// beside it (Part 7).
/// <para>
/// <b>A shape and not a coloured number</b>, because a number without a shape is only read once one
/// is already looking; the shape and colour show the trend, and only the number says how much time
/// is left. <b>A device without a battery shows nothing at all</b> - no empty symbol, no placeholder
/// number. And there is no message: the DM watches it fall and acts when it suits him; the red is
/// the last confirmation, not the first news.
/// </para>
/// </summary>
internal sealed class BatteryGauge : StackPanel
{
    private readonly Shape _shape = new();
    private readonly TextBlock _percent = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };

    internal BatteryGauge()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Visibility = Visibility.Collapsed;

        Children.Add(_shape);
        Children.Add(_percent);
    }

    /// <summary>Shows this battery, or nothing for a device without one or not connected.</summary>
    internal void Show(BatteryInfo? battery)
    {
        if (battery is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        _shape.Battery = battery;
        _percent.Text = string.Create(CultureInfo.CurrentCulture, $"{battery.Percent} %");
        Visibility = Visibility.Visible;
    }

    private sealed class Shape : FrameworkElement
    {
        private const double BodyWidth = 22;
        private const double BodyHeight = 11;
        private const double Nub = 2;

        private static readonly Brush Green = Frozen(Color.FromRgb(0x2E, 0x9E, 0x44));
        private static readonly Brush Red = Frozen(Color.FromRgb(0xD1, 0x34, 0x38));

        internal BatteryInfo? Battery
        {
            get;
            set
            {
                field = value;
                InvalidateVisual();
            }
        }

        protected override Size MeasureOverride(Size availableSize) => new(BodyWidth + Nub + 1, BodyHeight + 2);

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Battery is not { } battery)
            {
                return;
            }

            var outline = new Pen(Brushes.DimGray, 1);
            var body = new Rect(0.5, 1, BodyWidth, BodyHeight);

            drawingContext.DrawRoundedRectangle(null, outline, body, 2, 2);
            drawingContext.DrawRectangle(Brushes.DimGray, null, new Rect(BodyWidth + 0.5, 1 + (BodyHeight / 3), Nub, BodyHeight / 3));

            // The fill matches the percentage, so 60 % is visibly fuller than 20 % (step 37d1).
            var level = Math.Clamp(battery.Percent, 0, 100) / 100d;
            var fill = new Rect(2, 2.5, Math.Max(0, (BodyWidth - 3) * level), BodyHeight - 3);

            drawingContext.DrawRectangle(battery.Low ? Red : Green, null, fill);

            if (battery.PluggedIn)
            {
                // Without it 18 % would be an alarm while the device is charging - "falling" and
                // "rising" are different messages (Part 7).
                var bolt = new StreamGeometry();

                using (var draw = bolt.Open())
                {
                    var cx = BodyWidth / 2;

                    draw.BeginFigure(new Point(cx + 1.5, 1.5), isFilled: true, isClosed: true);
                    draw.LineTo(new Point(cx - 3, 7), isStroked: true, isSmoothJoin: false);
                    draw.LineTo(new Point(cx, 7), isStroked: true, isSmoothJoin: false);
                    draw.LineTo(new Point(cx - 1.5, 11.5), isStroked: true, isSmoothJoin: false);
                    draw.LineTo(new Point(cx + 3, 6), isStroked: true, isSmoothJoin: false);
                    draw.LineTo(new Point(cx, 6), isStroked: true, isSmoothJoin: false);
                }

                bolt.Freeze();
                drawingContext.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 0.8), bolt);
            }
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
