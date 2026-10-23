using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DnDOverlay.Core;
using CorePoint = DnDOverlay.Core.Point;
using CoreRect = DnDOverlay.Core.Rect;
using TilePoint = System.Windows.Point;

namespace DnDOverlay.Control;

/// <summary>
/// The handles of the foreign windows on a tile (Part 7): the title strip moves, a corner sizes, the
/// menu minimises, maximises, restores. <b>Taken hold of by the strip only</b> (DM, 21.10.2026): the
/// inside of a wireframe stays the selection frame's, or it would be gone over a full-screen MapTool.
/// <para>
/// <b>A layer of its own, lying on top, and answering only where it means something.</b> It is hit
/// only on a strip or a corner, and only where no picture lies - a picture on the strip is still the
/// picture's. Everywhere else the hand goes through to the tile underneath, which leaves alone what
/// starts here. What it draws is the outline of the window on its way; the wireframes themselves lie
/// behind the scene, in the thumbnail.
/// </para>
/// <para>
/// <b>Only when the device lets its windows be laid</b> - a device that only reports gets no handles,
/// and its wireframes say so when a hand tries.
/// </para>
/// </summary>
internal sealed class WindowGrips : FrameworkElement
{
    private readonly Press _press = new();
    private readonly Func<TilePoint, bool> _covered;

    private IReadOnlyList<ForeignWindow> _windows = [];
    private ViewRotation _view;
    private bool _controllable;

    private ForeignWindow? _held;
    private WindowGrip _grip;
    private CorePoint _from;
    private CoreRect? _now;
    private int? _finger;

    /// <param name="covered">Whether a picture lies at a place on the face - then the place is the picture's.</param>
    internal WindowGrips(Func<TilePoint, bool> covered) => _covered = covered;

    /// <summary>A window was laid: moved or sized to a place, or minimised, maximised, restored.</summary>
    internal event EventHandler<(ForeignWindow Window, WindowAction Action, CoreRect? Bounds)>? Laid;

    /// <summary>The window in the hand moved - the thumbnail draws it where the hand has it.</summary>
    internal event EventHandler? Moving;

    /// <summary>The window in the hand and where it is now, while a hand holds one.</summary>
    internal (long Handle, CoreRect Bounds)? Holding => _held is { } held && _now is { } now ? (held.Handle, now) : null;

    /// <summary>The windows of this screen, how the tile is turned, and whether they may be laid at all.</summary>
    internal void Show(IReadOnlyList<ForeignWindow> windows, ViewRotation view, bool controllable)
    {
        _windows = windows;
        _view = view;
        _controllable = controllable;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(hitTestParameters);

        return _held is not null || Take(hitTestParameters.HitPoint) is not null
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (_now is { } now)
        {
            drawingContext.DrawRectangle(
                brush: null,
                new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash },
                Placing.InTile(now, _view, RenderSize));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.StylusDevice is null && Begin(e.GetPosition(this)))
        {
            _ = CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.StylusDevice is null && _held is not null && _finger is null)
        {
            Follow(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Laid BEFORE the capture is let go: releasing raises LostMouseCapture on the spot, and that
        // abandons the hold - released first, the move was dropped every time (measured, M5 review).
        if (e.StylusDevice is null && _held is not null && _finger is null)
        {
            End();
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (Take(e.GetPosition(this)) is { } window)
        {
            Menu(window.Window);
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // Taken away mid-move - a menu, another window: nothing is laid.
        if (_finger is null)
        {
            Abandon();
        }
    }

    protected override void OnTouchDown(TouchEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var at = e.GetTouchPoint(this).Position;

        if (_finger is null && Take(at) is { } window && Begin(at))
        {
            _finger = e.TouchDevice.Id;
            _ = CaptureTouch(e.TouchDevice);

            // The long press is the menu, by the one rule (Press): it rings only if the finger stays.
            _press.Down(at, () =>
            {
                Abandon();
                Menu(window.Window);
            });
        }

        e.Handled = true;
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.TouchDevice.Id == _finger)
        {
            var at = e.GetTouchPoint(this).Position;

            _press.Moved(at);
            Follow(at);
        }

        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Laid before the capture goes, for the reason the mouse has (OnMouseLeftButtonUp).
        if (e.TouchDevice.Id == _finger)
        {
            _press.Up();
            _finger = null;
            End();
            _ = ReleaseTouchCapture(e.TouchDevice);
        }

        e.Handled = true;
    }

    /// <summary>
    /// The finger's counterpart of <see cref="OnLostMouseCapture"/>: the capture taken away without a
    /// <c>TouchUp</c> reaching here - the window deactivated mid-touch, a toast, the long-press menu.
    /// Without it the held window and the finger stayed set, the layer answered every hit on the face
    /// and the tile skips what starts on it: no picture, no selection, no menu any more, and the mouse
    /// grips were refused as well (M5 review).
    /// </summary>
    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.TouchDevice.Id == _finger)
        {
            _press.Up();
            _finger = null;
            Abandon();
        }
    }

    /// <summary>The window and grip at a place on the face - topmost first - or none: not on a grip, covered by a picture, or not to be laid.</summary>
    private (ForeignWindow Window, WindowGrip Grip)? Take(TilePoint at)
    {
        if (!_controllable || _windows.Count == 0 || _covered(at))
        {
            return null;
        }

        return WindowPick.At(_windows, Placing.InScene(at, _view, RenderSize), SceneThumbnail.Strip(RenderSize));
    }

    private bool Begin(TilePoint at)
    {
        if (Take(at) is not { } taken)
        {
            return false;
        }

        _held = taken.Window;
        _grip = taken.Grip;
        _from = Placing.InScene(at, _view, RenderSize);
        _now = null;

        return true;
    }

    private void Follow(TilePoint at)
    {
        if (_held is not { } held)
        {
            return;
        }

        var place = Placing.InScene(at, _view, RenderSize);

        if (_now is null && Math.Abs(place.X - _from.X) + Math.Abs(place.Y - _from.Y) < 0.005)
        {
            return;
        }

        _now = _grip is WindowGrip.Corner
            ? WindowGeometry.Resized(held.Bounds, _from, place)
            : WindowGeometry.Moved(held.Bounds, _from, place);

        InvalidateVisual();
        Moving?.Invoke(this, EventArgs.Empty);
    }

    private void End()
    {
        if (_held is { } held && _now is { } now)
        {
            Laid?.Invoke(this, (held, WindowAction.Move, now));
        }

        Abandon();
    }

    private void Abandon()
    {
        _held = null;
        _now = null;
        InvalidateVisual();
        Moving?.Invoke(this, EventArgs.Empty);
    }

    private void Menu(ForeignWindow window)
    {
        var menu = new ContextMenu { PlacementTarget = this };

        foreach (var (header, action) in new[] { (Strings.GripsMinimise, WindowAction.Minimize), (Strings.GripsMaximise, WindowAction.Maximize), (Strings.GripsRestore, WindowAction.Restore) })
        {
            var item = new MenuItem { Header = header };

            item.Click += (_, _) => Laid?.Invoke(this, (window, action, null));
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var fill = new MenuItem { Header = Strings.GripsFillScreen };

        fill.Click += (_, _) => Laid?.Invoke(this, (window, WindowAction.Move, new CoreRect(0, 0, 1, 1)));
        menu.Items.Add(fill);

        menu.IsOpen = true;
    }
}
