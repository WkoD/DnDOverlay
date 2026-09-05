using System.Windows;
using System.Windows.Media;
using DnDOverlay.Core;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// What the DM has picked out, drawn over the scene: an outline around every selected picture, and
/// the frame while one is being dragged.
/// <para>
/// <b>The selection circles are gone</b> (hand-run of M4, 25a). Part 7 took them from the Explorer
/// because touch has no platform-wide habit for "add to selection" - and at the table they turned
/// out to be in the way: on a tile the size of a thumbnail a circle on every picture is clutter
/// over the very thing being judged, and the coloured outline already says what is selected. The
/// two ways of collecting several that remain - the frame with either hand, and Ctrl+click with the
/// mouse - cover it.
/// </para>
/// <para>
/// <b>A layer of its own rather than part of the drawing</b>, for the same reason the loading fill
/// is one: the selection is the control's own view state and has no place in a
/// <see cref="SceneState"/>. The thumbnail draws a scene and must go on being able to draw a SAVED
/// one, which nobody has ever selected anything in (M5b).
/// </para>
/// </summary>
internal sealed class Marks : FrameworkElement
{
    private readonly Selection _selection;

    private TileRect? _frame;
    private bool _dimmed;

    private SceneState _scene = SceneState.Empty;
    private ScreenContext _screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
    private ViewRotation _view;

    internal Marks(Selection selection)
    {
        _selection = selection;

        // Every grip belongs to what lies underneath; this only draws.
        IsHitTestVisible = false;
        ClipToBounds = true;

        _selection.Changed += (_, _) => InvalidateVisual();
    }

    /// <summary>The scene these marks lie over. Arrives with the drawing below and follows it.</summary>
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
    /// Whether the pictures are to stand back, because the hand is working the layer beneath them.
    /// <b>The mode has to be visible or it is a trap</b>: a drag that moves everything at once and
    /// nothing the DM aimed at is the hardest kind of surprise to explain (Part 7).
    /// </summary>
    internal void Dimmed(bool dimmed)
    {
        _dimmed = dimmed;

        InvalidateVisual();
    }

    /// <summary>
    /// The frame being dragged right now, or <see langword="null"/>.
    /// <para>
    /// <b>It is drawn only as far as the tile goes</b>, and the clamping happens before it gets
    /// here (<see cref="TileFace"/>). A rectangle that grew on over the neighbouring screen and
    /// selected nothing there would promise something it cannot keep (Part 7).
    /// </para>
    /// </summary>
    internal void Frame(TileRect? frame)
    {
        _frame = frame;

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        base.OnRender(drawingContext);

        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        if (_dimmed)
        {
            // A border round the whole face and nothing over it. The pictures themselves go
            // see-through one layer down (SceneThumbnail.Faded) - a film here would have dimmed the
            // background along with them (hand-run of M4, 38b).
            drawingContext.DrawRectangle(
                brush: null,
                new Pen(Brushes.Gold, 2),
                new TileRect(1, 1, Math.Max(0, RenderSize.Width - 2), Math.Max(0, RenderSize.Height - 2)));
        }

        if (!_scene.ItemsVisible)
        {
            return;
        }

        if (_frame is { } frame)
        {
            // Filled as well as outlined: on a dark table a one-pixel line is hard to follow with a
            // finger on top of it, and the fill says which side of the line is inside.
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xBF, 0xFF)),
                new Pen(Brushes.DeepSkyBlue, 1) { DashStyle = DashStyles.Dash },
                frame);
        }

        var outline = new Pen(Brushes.DeepSkyBlue, 2);

        foreach (var item in _scene.Items)
        {
            if (_selection.Contains(item.ItemId))
            {
                drawingContext.DrawGeometry(brush: null, outline, Around(item));
            }
        }

    }

    /// <summary>
    /// The outline of the picture as it is actually drawn - the four corners, not the box around
    /// them. A box would stand away from a turned picture by half its diagonal and mark room the
    /// picture is not in.
    /// </summary>
    private StreamGeometry Around(SceneItem item)
    {
        var corners = Layout.ItemToQuad(item, _screen)
            .Select(corner => Placing.InTile(corner, _view, RenderSize))
            .ToList();

        var geometry = new StreamGeometry();

        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(corners[0], isFilled: false, isClosed: true);
            drawing.PolyLineTo(corners.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();

        return geometry;
    }
}
