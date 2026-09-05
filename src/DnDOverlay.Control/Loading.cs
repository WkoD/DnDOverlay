using System.Windows;
using System.Windows.Media;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using CoreRect = DnDOverlay.Core.Rect;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// What has not arrived at the table yet, drawn on the pictures themselves: an item stands
/// <b>desaturated</b> and fills from the bottom with its own colour as the picture reaches the
/// display - the whole picture is the progress bar.
/// <para>
/// <b>It is not a ring, and the reason is a calculation.</b> The ring at the table sizes itself as
/// <c>clamp(shorter edge × 0.6, 16, 56)</c> DIP and falls away entirely below 16 (M3, R1). In an
/// overview tile of about 170 DIP an item at scale 0.1 is 17 DIP tall, its shorter edge about 11 -
/// which makes a ring of 6.8 DIP, and therefore none at all. Prüfschritt 37b asks the tile to show
/// "FROM THE START that something is being transferred", and a fill needs no minimum size: it uses
/// the room the item has anyway. At nought per cent the picture is entirely grey, which is more
/// visible than any ring.
/// </para>
/// <para>
/// <b>The tile shows somebody else's progress.</b> The control has had the picture all along; what
/// is missing is missing AT THE TABLE. So the colour does not say "loading", it says "this is
/// already standing over there" - and with five targets, five tiles show five fills, which is what
/// Part 7 wants when it says the weakest wireless is visible rather than averaged away.
/// </para>
/// <para>
/// <b>Ungedrosselt</b>, unlike the scene below it (Part 7, rank 3 before 4): the geometry may lag
/// behind under load, the fill may not - it is the answer to "is anything happening at all". It is
/// therefore its own layer, fed from <c>AssetProgress</c> rather than from the scene stream.
/// </para>
/// <para>
/// <b>The edge is drawn as a line, not left to the colour.</b> Over a greyscale picture -
/// a black-and-white map, an ink portrait - desaturated and coloured are the same thing, and the
/// fill would be invisible exactly where the DM is looking hardest.
/// </para>
/// </summary>
internal sealed class Loading : FrameworkElement
{
    private readonly Pictures _pictures;
    private readonly Dictionary<AssetId, double> _fractions = [];

    private SceneState _scene = SceneState.Empty;
    private ScreenContext _screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
    private ViewRotation _view;

    internal Loading(Pictures pictures)
    {
        _pictures = pictures;

        // Nothing here takes a touch: it lies over the scene, and every grip belongs to what is
        // underneath.
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>
    /// The scene this lies over. It arrives with the drawing below and may be as late as that one -
    /// what must not be late is the fill, and that comes in through <see cref="Report"/>.
    /// </summary>
    internal void Show(SceneState scene, ScreenContext screen, ViewRotation view)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        _scene = scene;
        _screen = screen;
        _view = view;

        InvalidateVisual();
    }

    /// <summary>
    /// What the device of this screen is loading. Drawn straight away rather than at the next
    /// bundled pass - that is the whole point of this layer.
    /// </summary>
    internal void Report(IReadOnlyList<AssetLoad> loads)
    {
        ArgumentNullException.ThrowIfNull(loads);

        _fractions.Clear();

        foreach (var load in loads)
        {
            // Done is not drawn: a picture that is there is a picture, and a fill standing at the
            // top for a second afterwards would say something is still happening. Failed is not
            // drawn either - a placeholder with a reason belongs on the item, and that is M5a.
            if (load.State is AssetLoadState.Done or AssetLoadState.Failed)
            {
                continue;
            }

            _fractions[load.Asset] = Math.Clamp(load.Fraction, 0, 1);
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        base.OnRender(drawingContext);

        if (_fractions.Count == 0 || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        if (_scene.BackgroundVisible
            && _scene.Background is { } background
            && _fractions.TryGetValue(background.AssetId, out var behind))
        {
            Draw(drawingContext, Layout.BackgroundRect(background, _screen), background.RotationDeg, background.AssetId, behind);
        }

        if (!_scene.ItemsVisible)
        {
            return;
        }

        foreach (var item in _scene.Items.OfType<ImageItem>())
        {
            if (_fractions.TryGetValue(item.AssetId, out var fraction))
            {
                Draw(drawingContext, Layout.ItemToRect(item, _screen), item.RotationDeg, item.AssetId, fraction);
            }
        }
    }

    /// <summary>
    /// One picture, grey above the line and untouched below it. The line runs along the screen's
    /// bottom rather than the picture's own edge - a turned picture filling sideways would look
    /// like a fault, and it is the same decision as the ring's counter-rotation at the table (M3).
    /// </summary>
    private void Draw(
        DrawingContext drawingContext, CoreRect normalised, double angleDeg, AssetId asset, double fraction)
    {
        var grey = _pictures.Grey(asset);

        if (grey is null)
        {
            return;
        }

        var rect = Placing.InTile(normalised, _view, RenderSize);

        // What is still missing is the part ABOVE the fill line, measured over the whole item -
        // including what a rotation pushes past its own rectangle, or a turned picture would keep
        // a grey corner after the fill was full.
        var hull = Hull(rect, Viewing.AngleInView(angleDeg, _view));
        var line = hull.Bottom - (hull.Height * fraction);

        drawingContext.PushClip(new RectangleGeometry(new TileRect(hull.Left, hull.Top, hull.Width, Math.Max(0, line - hull.Top))));
        drawingContext.PushTransform(
            new RotateTransform(
                Viewing.AngleInView(angleDeg, _view),
                rect.X + (rect.Width / 2),
                rect.Y + (rect.Height / 2)));

        drawingContext.DrawImage(grey, rect);

        drawingContext.Pop();
        drawingContext.Pop();

        if (line > hull.Top && line < hull.Bottom)
        {
            drawingContext.DrawLine(
                new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dot },
                new TilePoint(hull.Left, line),
                new TilePoint(hull.Right, line));
        }
    }

    /// <summary>The axis-parallel hull of the turned rectangle - what the fill line measures against.</summary>
    private static TileRect Hull(TileRect rect, double angleDeg)
    {
        var radians = angleDeg * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));

        var width = (rect.Width * cos) + (rect.Height * sin);
        var height = (rect.Width * sin) + (rect.Height * cos);

        return new TileRect(
            rect.X + ((rect.Width - width) / 2),
            rect.Y + ((rect.Height - height) / 2),
            width,
            height);
    }
}
