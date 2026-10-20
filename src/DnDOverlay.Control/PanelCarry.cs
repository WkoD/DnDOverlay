using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DnDOverlay.Core;
using ScreenPoint = System.Windows.Point;

namespace DnDOverlay.Control;

/// <summary>What is carried out of the panel: pictures of the stock, one saved scene, or one layout.</summary>
/// <param name="Pictures">The pictures, in the order they were chosen - empty for a scene or a layout.</param>
/// <param name="Scene">The scene's name, or none.</param>
/// <param name="Look">What the ghost shows.</param>
/// <param name="Layout">The layout's name, or none - a layout has no single target, so the whole stage is its target.</param>
internal sealed record CarryLoad(IReadOnlyList<AssetRef> Pictures, string? Scene, ImageSource? Look, string? Layout = null);

/// <summary>
/// One entry - or the whole selection - on its way from the panel to a tile (Part 7: "drag goes both
/// ways"). The source feeds it the hand's positions, the window decides at the end where it landed.
/// <para>
/// <b>Over the whole window, not over the stage</b>: the ghost starts above the panel and has to stay
/// visible all the way across, so it lies on the adorner layer of the window's content, which both
/// share. The stage's own carry from tile to tile keeps its own layer; the ghost is the same type.
/// </para>
/// <para>
/// <b>The source keeps the hand</b> - the card holds the mouse capture, the grid holds the finger -
/// and that is why a retracted panel slides away rather than disappearing when the carry begins: an
/// element that is collapsed loses its capture, and the carry would end the moment it started.
/// </para>
/// </summary>
internal sealed class PanelCarry
{
    private readonly UIElement _over;

    private Carrying? _ghost;
    private CarryLoad? _load;

    internal PanelCarry(UIElement over) => _over = over;

    /// <summary>Whether something is on its way.</summary>
    internal bool Carrying => _load is not null;

    /// <summary>What is on its way, if anything.</summary>
    internal CarryLoad? Load => _load;

    /// <summary>A carry began - a retracted panel goes back, the tiles show where a background lands.</summary>
    internal event EventHandler? Began;

    /// <summary>The hand moved, in screen coordinates.</summary>
    internal event EventHandler<ScreenPoint>? Moved;

    /// <summary>The hand let go, in screen coordinates - the window decides what lands where.</summary>
    internal event EventHandler<(CarryLoad Load, ScreenPoint At)>? Dropped;

    /// <summary>The carry is over, dropped or not.</summary>
    internal event EventHandler? Ended;

    internal void Begin(CarryLoad load, ScreenPoint at)
    {
        End();

        _load = load;
        _ghost = new Carrying(_over, load.Look, Math.Max(1, load.Pictures.Count));

        AdornerLayer.GetAdornerLayer(_over)?.Add(_ghost);
        _ghost.At(_over.PointFromScreen(at));

        Began?.Invoke(this, EventArgs.Empty);
    }

    internal void Move(ScreenPoint at)
    {
        if (_ghost is null)
        {
            return;
        }

        _ghost.At(_over.PointFromScreen(at));
        Moved?.Invoke(this, at);
    }

    internal void Drop(ScreenPoint at)
    {
        if (_load is not { } load)
        {
            return;
        }

        // Asked while the strips still stand: where it landed is read off them.
        Dropped?.Invoke(this, (load, at));
        End();
    }

    /// <summary>Ends without landing - the capture was lost, a menu took the hand.</summary>
    internal void End()
    {
        if (_ghost is { } ghost)
        {
            AdornerLayer.GetAdornerLayer(_over)?.Remove(ghost);
        }

        var was = _load is not null;

        _ghost = null;
        _load = null;

        if (was)
        {
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }
}
