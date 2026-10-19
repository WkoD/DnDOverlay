using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// What is under the hand while it is being carried from one place to another - a ghost that
/// follows and belongs to nothing underneath it.
/// <para>
/// <b>It exists because WPF's own drag does not start under a finger.</b>
/// <c>DragDrop.DoDragDrop</c> stops working the moment <c>IsManipulationEnabled</c> is on, and it
/// is on wherever a picture can be pushed around - which is every tile (Part 7 names the trap).
/// So every drag INSIDE this program runs over this layer; <c>DragDrop</c> keeps only what comes
/// from outside it.
/// </para>
/// <para>
/// <b>It carries a picture and a payload, not an item.</b> M5b puts the stock's entries on it in
/// both directions and the wireframes of foreign windows too - those are resized at their corners
/// and minimised by a long press (Part 10 says in as many words that they build on this layer). One
/// that knew only an <c>ItemId</c> would be a second one by then.
/// </para>
/// <para>
/// <b>An adorner rather than a window</b>: it lies over everything in the same window without
/// taking part in its layout, so nothing moves aside while a picture is on its way across.
/// </para>
/// </summary>
internal sealed class Carrying : Adorner
{
    private readonly ImageSource? _look;
    private readonly int _count;

    private TilePoint _at;

    /// <summary>
    /// How tall the ghost is, in DIP. Large enough to be seen from under a hand, small enough that
    /// it does not cover the tile it is being carried to - which is the tile the DM is aiming at.
    /// </summary>
    private const double Tall = 80;

    /// <param name="over">The element whose adorner layer carries the ghost.</param>
    /// <param name="look">The picture shown, or none for a grey stand-in.</param>
    /// <param name="count">How many are on their way - more than one is written on the ghost, because the picture shows only the first.</param>
    internal Carrying(UIElement over, ImageSource? look, int count = 1)
        : base(over)
    {
        _look = look;
        _count = count;

        // It answers nothing: what is under the hand is decided by what lies beneath this, and a
        // ghost that took the hit test would always be the answer.
        IsHitTestVisible = false;
    }

    /// <summary>Where the hand is now, in the coordinates of the element this lies over.</summary>
    internal void At(TilePoint at)
    {
        _at = at;

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        base.OnRender(drawingContext);

        var shape = _look is { Height: > 0 } picture ? picture.Width / picture.Height : 1;
        var width = Tall * (shape <= 0 ? 1 : shape);

        // Centred on the hand rather than hanging off it: the DM aims with the middle of what he is
        // carrying, and an offset ghost would land a picture beside the place he pointed at.
        var place = new TileRect(_at.X - (width / 2), _at.Y - (Tall / 2), width, Tall);

        drawingContext.PushOpacity(0.7);

        if (_look is null)
        {
            drawingContext.DrawRectangle(Brushes.DimGray, new Pen(Brushes.White, 1), place);
        }
        else
        {
            drawingContext.DrawImage(_look, place);
            drawingContext.DrawRectangle(brush: null, new Pen(Brushes.White, 1), place);
        }

        drawingContext.Pop();

        if (_count > 1)
        {
            var text = new FormattedText(
                _count.ToString(System.Globalization.CultureInfo.CurrentCulture),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var badge = new TileRect(place.Right - text.Width - 10, place.Top - 8, text.Width + 12, text.Height + 4);

            drawingContext.DrawRoundedRectangle(Brushes.SteelBlue, null, badge, 6, 6);
            drawingContext.DrawText(text, new TilePoint(badge.Left + 6, badge.Top + 2));
        }
    }
}
