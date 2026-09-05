using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// The face of a tile: the three layers that show one screen, laid one exactly over the other, and
/// every grip that lands on the scene.
/// <para>
/// <b>The layers are a panel of its own because they must share one rectangle.</b> They are three
/// elements rather than one drawing on purpose - the scene is bundled to one pass, the loading fill
/// is not, and the marks belong to the control rather than to the scene (Part 7, rank 3 before 4) -
/// but a mark half a pixel off its picture is a mark on the wrong picture. Left to a <c>Grid</c>
/// they would each take the whole slot and the drawing would stretch out of shape in the single
/// view, where the tile is wider than the table.
/// </para>
/// <para>
/// <b>The face keeps the table's shape</b>, turned the way the DM looks at it
/// (<see cref="Viewing.AspectRatioInView"/>): a table seen from its short side is upright in the
/// tile, and a face that stayed landscape would stretch everything on it.
/// </para>
/// <para>
/// <b>Grips arrive here and nowhere else.</b> The head is the tile's other half and carries the
/// arrangement drag; everything that means a place on the table means a place on this face.
/// </para>
/// </summary>
internal sealed class TileFace : Panel
{
    private readonly SceneThumbnail _thumbnail;
    private readonly Loading _loading;
    private readonly Marks _marks;
    private readonly Selection _selection;

    private SceneState _scene = SceneState.Empty;
    private ScreenContext _screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
    private ViewRotation _view;

    private TilePoint? _pressed;

    /// <summary>
    /// How far a hand may travel and still have meant a tap, in DIP.
    /// <para>
    /// Part 7 asks for it by name - "a short twitch counts as a tap and clears the selection"
    /// (Prüfschritt 25a) - because without it every touch would draw a one-pixel frame and throw
    /// the selection away.
    /// </para>
    /// </summary>
    internal const double TapTravel = 6;

    internal TileFace(Pictures pictures, Selection selection)
    {
        _selection = selection;
        _thumbnail = new SceneThumbnail(pictures);
        _loading = new Loading(pictures);
        _marks = new Marks(selection);

        Children.Add(_thumbnail);
        Children.Add(_loading);
        Children.Add(_marks);

        ClipToBounds = true;

        // Mouse and finger are wired apart rather than left to WPF's promotion of touch to mouse:
        // that promotion stops the moment manipulation is switched on, and it is switched on here.
        PreviewMouseLeftButtonDown += (_, pressed) => Down(pressed.GetPosition(this));
        PreviewMouseLeftButtonUp += (_, released) => Up(released.GetPosition(this));

        PreviewTouchDown += (_, pressed) => Down(pressed.GetTouchPoint(this).Position);
        PreviewTouchUp += (_, released) => Up(released.GetTouchPoint(this).Position);
    }

    /// <summary>Raised when a grip on this face has changed what is selected on this screen.</summary>
    internal event EventHandler? Touched;

    /// <summary>
    /// Whether this face has the whole room of an open tile rather than its own small height.
    /// </summary>
    internal bool Opened
    {
        get;
        set
        {
            field = value;
            InvalidateMeasure();
        }
    }

    /// <summary>How tall a face is in the overview, in DIP. The width follows the table's shape.</summary>
    internal const double Small = 150;

    /// <summary>What this face shows from now on.</summary>
    internal void Show(SceneState scene, ScreenContext screen, ViewRotation view)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        var before = Shape();

        _scene = scene;
        _screen = screen;
        _view = view;

        // A picture that has left this screen is no longer selected - a menu command to an item
        // that is not there would be ineffective at the hub and a broken promise in the surface.
        _selection.Keep(scene);

        _thumbnail.Show(scene, screen, view);
        _loading.Show(scene, screen, view);
        _marks.Show(scene, screen, view);

        // Only when the table itself changed shape - a screen re-plugged at another resolution, or
        // the view turned. Every arriving patch asking for a new measure would put the whole stage
        // through a layout pass sixty times a second, which is what the bundling is there to avoid.
        if (before != Shape())
        {
            InvalidateMeasure();
        }

        Redraw.Ask(_thumbnail);
    }

    /// <summary>What the device of this screen is loading. Straight through, ungoverned by the bundling.</summary>
    internal void Report(IReadOnlyList<AssetLoad> loads) => _loading.Report(loads);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var wanted = Wanted(availableSize);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(wanted);
        }

        return wanted;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Centred, so an open tile that is wider than the table has its margin on both sides. The
        // layers all get the SAME rectangle, which is the whole reason this panel exists.
        var face = Wanted(finalSize);
        var place = new TileRect(
            Math.Max(0, (finalSize.Width - face.Width) / 2),
            Math.Max(0, (finalSize.Height - face.Height) / 2),
            face.Width,
            face.Height);

        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(place);
        }

        return finalSize;
    }

    /// <summary>
    /// The largest rectangle of the table's shape that fits in what is offered.
    /// <para>
    /// In the overview the height is fixed and leads: the tiles are rows of a wrapping arrangement,
    /// and rows of unequal height leave holes in it (Part 7).
    /// </para>
    /// </summary>
    private Size Wanted(Size available)
    {
        var shape = Shape();

        if (shape <= 0)
        {
            return new Size(0, 0);
        }

        var height = Opened
            ? double.IsInfinity(available.Height) ? Small : available.Height
            : Small;

        var width = height * shape;

        return double.IsInfinity(available.Width) || width <= available.Width
            ? new Size(width, height)
            : new Size(available.Width, available.Width / shape);
    }

    /// <summary>
    /// The shape the table has as the DM sees it - <b>asked of the drawing rather than worked out
    /// here</b>. Two answers to that question would put the marks on a rectangle the picture is not
    /// on (rule 9).
    /// </summary>
    private double Shape() => _thumbnail.AspectRatio;

    /// <summary>Where the face itself lies inside this panel - what a grip has to subtract.</summary>
    private TilePoint OnFace(TilePoint at)
    {
        var face = Wanted(RenderSize);

        return new TilePoint(
            at.X - Math.Max(0, (RenderSize.Width - face.Width) / 2),
            at.Y - Math.Max(0, (RenderSize.Height - face.Height) / 2));
    }

    private void Down(TilePoint at) => _pressed = at;

    /// <summary>
    /// A hand let go. <b>Only a tap selects</b> - anything that travelled was a drag and belongs to
    /// whoever handles drags; without that rule every attempt to move a picture would first select
    /// something else.
    /// </summary>
    private void Up(TilePoint at)
    {
        if (_pressed is not { } from)
        {
            return;
        }

        _pressed = null;

        if (Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) > TapTravel)
        {
            return;
        }

        Tap(OnFace(at), Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    /// <summary>
    /// One tap, and the cascade it runs through: the selection circle first, then the scene, then
    /// free area.
    /// <para>
    /// <b>The circle comes first because it lies ON a picture.</b> Asked the other way round it
    /// could never be reached - the picture under it would always answer - and the touch way of
    /// building a selection would silently be the mouse's Ctrl+click only (Part 7).
    /// </para>
    /// </summary>
    private void Tap(TilePoint at, bool adding)
    {
        var face = Wanted(RenderSize);

        if (_marks.CircleAt(at) is { } circled)
        {
            _selection.Toggle(circled);
        }
        else if (Picking.At(_scene, _screen, Placing.InScene(at, _view, face)) is { } item)
        {
            if (adding)
            {
                _selection.Toggle(item);
            }
            else
            {
                _selection.Only(item);
            }
        }
        else
        {
            // Free area clears it. Ctrl does not save it: a modifier that changed what an empty
            // place means would be a rule nobody could see (Part 7).
            _selection.Clear();
        }

        Touched?.Invoke(this, EventArgs.Empty);
    }
}
