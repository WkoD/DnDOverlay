using System.Windows;
using System.Windows.Media;
using DnDOverlay.Core;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// What the DM has picked out, drawn over the scene: an outline around every selected picture and,
/// <b>as soon as anything is selected at all</b>, a small circle on every picture (Part 7).
/// <para>
/// <b>The circles appear with the first selection and not before.</b> That is the Explorer's habit,
/// taken over deliberately because touch has no platform-wide one for "add to selection": there is
/// no mode to switch, nothing to remember, and the circles go away with the selection that brought
/// them. Before the first tap the tile is a picture of the table and nothing else.
/// </para>
/// <para>
/// <b>The circles are drawn and hit here, in one place.</b> A second computation of where they sit
/// would be the fan's mistake from M3 all over again - laid out by one formula, picked by another,
/// and the picture that fell between them was invisible and unreachable at once (Guide
/// <c>G22</c>).
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

    /// <summary>
    /// The diameter of a selection circle, in DIP.
    /// <para>
    /// <b>Fixed rather than scaled with the picture</b>, because it is aimed at with a finger and a
    /// finger is the same size on a large item and a small one. It is also the one number here that
    /// misses a measured minimum knowingly: 96 DIP is what an imprecise grip wants (Guide
    /// <c>G23</c>), and an overview tile is 150 DIP tall in total, so a circle per picture cannot
    /// have it. The single view is where this grip has the room it needs, and the frame is the way
    /// to collect several at once in the overview.
    /// </para>
    /// </summary>
    private const double Circle = 16;

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

    /// <summary>
    /// The picture whose circle lies under this place, or <see langword="null"/>. Asked before the
    /// scene itself is asked: the circle lies ON a picture, so whoever tests the picture first
    /// never reaches the circle.
    /// </summary>
    internal ItemId? CircleAt(TilePoint at)
    {
        if (!_selection.Any || !_scene.ItemsVisible)
        {
            return null;
        }

        var radius = Circle / 2;

        // Topmost first, the same order the point cascade uses: where two circles overlap, the one
        // the eye sees on top is the one that answers.
        foreach (var item in _scene.Items.OrderByDescending(item => Parking.Depth(_scene, item)))
        {
            var centre = Where(item);

            if (((at.X - centre.X) * (at.X - centre.X)) + ((at.Y - centre.Y) * (at.Y - centre.Y))
                <= radius * radius)
            {
                return item.ItemId;
            }
        }

        return null;
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
            // A veil over the pictures and a border round the whole face: what is being worked on
            // is what is NOT dimmed, which is the background showing through.
            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(0x88, 0x10, 0x10, 0x10)),
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

        if (!_selection.Any)
        {
            return;
        }

        foreach (var item in _scene.Items)
        {
            var taken = _selection.Contains(item.ItemId);

            drawingContext.DrawEllipse(
                taken ? Brushes.DeepSkyBlue : new SolidColorBrush(Color.FromArgb(0xAA, 0x20, 0x20, 0x20)),
                new Pen(Brushes.White, 1),
                Where(item),
                Circle / 2,
                Circle / 2);
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

    /// <summary>
    /// Where a picture's circle sits: at the top left of its hull, as the DM sees it, pulled far
    /// enough in that the whole circle lies on the picture.
    /// <para>
    /// <b>Against the hull and not the corner of the turned picture</b>, so the circles of a row of
    /// pictures stay in a row whatever angle they lie at - they are a control, and a control that
    /// wanders with the rotation is one that has to be looked for.
    /// </para>
    /// <para>
    /// Kept inside the tile, because an item may lie half over the edge (Part 6 allows it): a
    /// circle outside the tile would belong to a picture that is on show and could not be reached.
    /// </para>
    /// </summary>
    private TilePoint Where(SceneItem item)
    {
        var hull = Placing.InTile(Layout.ItemToHullRect(item, _screen), _view, RenderSize);
        var radius = Circle / 2;

        return new TilePoint(
            Math.Clamp(hull.X + radius, radius, Math.Max(radius, RenderSize.Width - radius)),
            Math.Clamp(hull.Y + radius, radius, Math.Max(radius, RenderSize.Height - radius)));
    }
}
