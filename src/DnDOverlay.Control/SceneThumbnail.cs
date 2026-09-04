using System.Windows;
using System.Windows.Media;
using DnDOverlay.Core;
using CoreRect = DnDOverlay.Core.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// One scene, drawn the way it lies - and nothing else. It knows a <see cref="SceneState"/>, a
/// <see cref="ScreenContext"/> and how the DM is looking at them; it knows nothing about tiles,
/// about the hub, or about which screen this is.
/// <para>
/// <b>That cut is the point.</b> M4 needs the drawing once, M5 three times more: the diagnostic
/// view REPLACES it inside a tile, the scenes tab shows a preview of a SAVED scene, and a layout
/// card shows several of them side by side - "the same rendering as everywhere, only several times
/// and smaller", which Part 7 calls the actual work of that tab. Built into the tile, each of those
/// would be an operation on the open heart.
/// </para>
/// <para>
/// <b>Everything goes through <see cref="Layout.ItemToRect"/></b> (rule 9), and the view rotation is
/// applied to the result rather than to the model: turning the view moves nothing on the table, so
/// nothing about the scene may change here (Part 7).
/// </para>
/// <para>
/// <b>Drawn in one pass, not one element per item.</b> Three pictures moved at the table are about
/// sixty patches a second; a tree of elements re-arranged on each of them is what makes an overview
/// stutter exactly when something is happening (Part 7). What arrives changes the state, and the
/// state is drawn once per render pass - see <see cref="Redraw"/>.
/// </para>
/// </summary>
internal sealed class SceneThumbnail : FrameworkElement
{
    private readonly Pictures _pictures;

    private SceneState _scene = SceneState.Empty;
    private ScreenContext _screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
    private ViewRotation _view;

    internal SceneThumbnail(Pictures pictures)
    {
        _pictures = pictures;

        // The tile decides how large it is; what is drawn scales into whatever it gets.
        ClipToBounds = true;
    }

    /// <summary>The shape the drawing wants, so the tile can give it the room a turned table needs.</summary>
    internal double AspectRatio => Viewing.AspectRatioInView(_screen.AspectRatio, _view);

    /// <summary>
    /// What to draw from now on. It does not draw - the redraw does, once per render pass, so that
    /// twenty arriving patches cost one drawing rather than twenty.
    /// </summary>
    internal void Show(SceneState scene, ScreenContext screen, ViewRotation view)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        _scene = scene;
        _screen = screen;
        _view = view;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The thumbnail keeps the screen's shape, and it keeps the shape as the DM SEES it: a table
    /// turned by a quarter is upright in the tile, and a tile that stayed landscape would stretch
    /// everything drawn in it (<see cref="Viewing.AspectRatioInView"/>).
    /// </remarks>
    protected override Size MeasureOverride(Size availableSize)
    {
        var shape = AspectRatio;

        if (shape <= 0)
        {
            return new Size(0, 0);
        }

        // Height leads, because a tile is a row in a wrapping arrangement: the rows have to be of
        // one height or the arrangement gets holes in it (Part 7).
        var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        var width = height * shape;

        return double.IsInfinity(availableSize.Width) || width <= availableSize.Width
            ? new Size(width, height)
            : new Size(availableSize.Width, availableSize.Width / shape);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        base.OnRender(drawingContext);

        var size = RenderSize;

        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // The ground is drawn whatever else happens: an empty screen is a screen, and a tile that
        // showed nothing at all would look like a tile that is broken.
        drawingContext.DrawRectangle(
            Brushes.Black, pen: null, new System.Windows.Rect(0, 0, size.Width, size.Height));

        if (_scene.BackgroundVisible && _scene.Background is { } background)
        {
            Draw(
                drawingContext,
                Layout.BackgroundRect(background, _screen),
                background.RotationDeg,
                _pictures.For(background.AssetId),
                size);
        }

        if (!_scene.ItemsVisible)
        {
            return;
        }

        // The fan lies above the whole table, and the depth says so. Ordering by it here means the
        // thumbnail and the table cover the same things - one calculation, two surfaces (rule 9).
        foreach (var item in _scene.Items.OrderBy(item => Parking.Depth(_scene, item)))
        {
            Draw(
                drawingContext,
                Layout.ItemToRect(item, _screen),
                item.RotationDeg,
                item is ImageItem image ? _pictures.For(image.AssetId) : null,
                size);
        }
    }

    /// <summary>
    /// One picture, from normalised scene coordinates into this element. The rectangle turns with
    /// the view, the angle turns with it too, and the picture is stretched into what comes out -
    /// the anisotropy of normalised coordinates is the tile's shape, not the picture's problem
    /// (<see cref="Viewing"/>).
    /// </summary>
    private void Draw(
        DrawingContext drawingContext, CoreRect normalised, double angleDeg, ImageSource? picture, Size size)
    {
        var seen = Viewing.ToView(normalised, _view);

        var rect = new System.Windows.Rect(
            seen.X * size.Width,
            seen.Y * size.Height,
            Math.Max(0, seen.Width * size.Width),
            Math.Max(0, seen.Height * size.Height));

        var centre = new System.Windows.Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

        // The angle turns with the view as well: a picture standing straight on a table seen from
        // the other side is upside down, and drawing it otherwise would make the thumbnail a
        // different table rather than the same one from another side.
        drawingContext.PushTransform(
            new RotateTransform(Viewing.AngleInView(angleDeg, _view), centre.X, centre.Y));

        if (picture is null)
        {
            // No preview to hand: the arrangement is still the truth, so the place is drawn and
            // says "something lies here" rather than nothing at all.
            drawingContext.DrawRectangle(Brushes.DimGray, new Pen(Brushes.Gainsboro, 1), rect);
        }
        else
        {
            drawingContext.DrawImage(picture, rect);
        }

        drawingContext.Pop();
    }
}
