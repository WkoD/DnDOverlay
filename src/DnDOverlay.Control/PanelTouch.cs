using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using TilePoint = System.Windows.Point;

namespace DnDOverlay.Control;

/// <summary>
/// The hand on one of the panel's grids: a finger scrolls, pulls out, taps, holds; a mouse pulls out.
/// One per tab, both feeding the same <see cref="PanelCarry"/>.
/// <para>
/// <b>Scrolling and pulling out are told apart by direction</b> (Part 7, <see cref="PanelGesture"/>),
/// and that is why the grid scrolls under the finger by this hand rather than by the scroll viewer's
/// own panning: that panning takes the touch the moment it starts and leaves nothing to decide.
/// </para>
/// <para>
/// <b>The finger's taps are counted here too</b>, because the touch is taken before WPF can promote
/// it to a click - a promoted press would select the entry the moment the finger went down, and a
/// stroke that only scrolled would leave a selection behind (step 40g: "nothing is chosen"). So a tap
/// is a finger that came up without having moved, and a double tap two of them on the same entry.
/// </para>
/// <para>
/// <b>The long press is <see cref="Press"/>'s</b>, the one rule at the one place - not Windows'
/// press-and-hold, which is switched off on the grid so that the two cannot both answer.
/// </para>
/// </summary>
internal sealed partial class PanelTouch
{
    /// <summary>How far apart two taps may land and still be one double tap, in DIP.</summary>
    private const double TapsApart = 20;

    private readonly ScrollViewer _scroller;
    private readonly PanelCarry _carry;
    private readonly Press _press = new();
    private readonly Dictionary<FrameworkElement, Func<CarryLoad?>> _cards = [];

    private int? _finger;
    private TilePoint _from;
    private double _offsetFrom;
    private PanelStroke _stroke;
    private FrameworkElement? _card;
    private bool _held;
    private bool _carrying;

    private FrameworkElement? _lastTap;
    private TilePoint _lastTapAt;
    private long _lastTapMs;

    private (FrameworkElement Card, TilePoint From)? _mousePress;
    private bool _mouseCarrying;

    internal PanelTouch(ScrollViewer scroller, FrameworkElement surface, PanelCarry carry)
    {
        _scroller = scroller;
        _carry = carry;

        scroller.PanningMode = PanningMode.None;
        Stylus.SetIsPressAndHoldEnabled(surface, false);

        surface.PreviewTouchDown += (_, down) => Down(surface, down);
        surface.PreviewTouchMove += (_, moved) => Moved(moved);
        surface.PreviewTouchUp += (_, up) => Up(surface, up);
        surface.LostTouchCapture += (_, lost) =>
        {
            if (lost.TouchDevice.Id == _finger)
            {
                Reset(abandon: true);
            }
        };
    }

    /// <summary>The edge the panel docks at - it says which way across is toward the stage.</summary>
    internal PanelSide Side { get; set; } = PanelSide.Right;

    /// <summary>A finger tapped an entry once.</summary>
    internal event EventHandler<FrameworkElement>? Tapped;

    /// <summary>A finger tapped the same entry twice in quick succession.</summary>
    internal event EventHandler<FrameworkElement>? DoubleTapped;

    /// <summary>A finger rested on an entry long enough for its menu.</summary>
    internal event EventHandler<FrameworkElement>? Held;

    /// <summary>A finger swiped toward the window's edge - a retracted panel goes back.</summary>
    internal event EventHandler? SwipedIn;

    /// <summary>The grid is drawn anew: the entries known so far are gone.</summary>
    internal void Forget() => _cards.Clear();

    /// <summary>
    /// An entry of the grid, and what a carry out of it takes along - asked at the moment the carry
    /// begins, so it is the selection as it stands then. Null carries nothing.
    /// </summary>
    internal void Card(FrameworkElement card, Func<CarryLoad?> load)
    {
        _cards[card] = load;

        // The mouse needs no question of direction: the wheel scrolls (Part 7). A press that travels
        // past the tolerance pulls the entry out.
        card.PreviewMouseLeftButtonDown += (_, down) =>
        {
            if (down.StylusDevice is null)
            {
                _mousePress = (card, down.GetPosition(_scroller));
            }
        };

        card.PreviewMouseMove += (_, moved) =>
        {
            if (moved.StylusDevice is not null || _mousePress is not { } press || !ReferenceEquals(press.Card, card))
            {
                return;
            }

            if (moved.LeftButton is not MouseButtonState.Pressed)
            {
                _mousePress = null;
                return;
            }

            var at = moved.GetPosition(_scroller);

            if (_mouseCarrying)
            {
                _carry.Move(_scroller.PointToScreen(at));
                return;
            }

            if (Math.Abs(at.X - press.From.X) + Math.Abs(at.Y - press.From.Y) <= Press.Tolerance)
            {
                return;
            }

            if (load() is not { } taken)
            {
                _mousePress = null;
                return;
            }

            _mouseCarrying = true;
            _ = card.CaptureMouse();
            _carry.Begin(taken, _scroller.PointToScreen(at));
        };

        card.PreviewMouseLeftButtonUp += (_, up) =>
        {
            _mousePress = null;

            if (up.StylusDevice is not null || !_mouseCarrying)
            {
                return;
            }

            // Flag first, release second: the release raises LostMouseCapture, and that must not read
            // as an abandoned carry.
            _mouseCarrying = false;
            card.ReleaseMouseCapture();
            _carry.Drop(_scroller.PointToScreen(up.GetPosition(_scroller)));
            up.Handled = true;
        };

        card.LostMouseCapture += (_, _) =>
        {
            if (_mouseCarrying)
            {
                _mouseCarrying = false;
                _carry.End();
            }
        };
    }

    /// <summary>Whether the last press ended a carry - the card's click must not also select or send.</summary>
    internal bool MouseCarrying => _mouseCarrying;

    private bool Lying => Side is PanelSide.Top or PanelSide.Bottom;

    private void Down(FrameworkElement surface, TouchEventArgs down)
    {
        down.Handled = true;

        // One finger at a time: a second one would make two strokes out of one list.
        if (_finger is not null)
        {
            return;
        }

        _finger = down.TouchDevice.Id;
        _from = down.GetTouchPoint(_scroller).Position;
        _offsetFrom = Lying ? _scroller.HorizontalOffset : _scroller.VerticalOffset;
        _stroke = PanelStroke.Undecided;
        _held = false;
        _carrying = false;
        _card = CardOf(down.OriginalSource as DependencyObject);

        _ = surface.CaptureTouch(down.TouchDevice);

        if (_card is { } card)
        {
            _press.Down(_from, () =>
            {
                _held = true;
                Held?.Invoke(this, card);
            });
        }
    }

    private void Moved(TouchEventArgs moved)
    {
        if (moved.TouchDevice.Id != _finger)
        {
            return;
        }

        moved.Handled = true;

        var at = moved.GetTouchPoint(_scroller).Position;

        _press.Moved(at);

        if (_held)
        {
            return;
        }

        if (_stroke is PanelStroke.Undecided)
        {
            // Decided once, at the first millimetres, and kept (Part 7).
            _stroke = PanelGesture.Decide(at.X - _from.X, at.Y - _from.Y, Side);

            if (_stroke is PanelStroke.Out && _card is { } card && _cards.GetValueOrDefault(card)?.Invoke() is { } load)
            {
                _carrying = true;
                _carry.Begin(load, _scroller.PointToScreen(at));
            }
            else if (_stroke is PanelStroke.In)
            {
                SwipedIn?.Invoke(this, EventArgs.Empty);
            }
        }

        switch (_stroke)
        {
            case PanelStroke.Scroll when Lying:
                _scroller.ScrollToHorizontalOffset(_offsetFrom - (at.X - _from.X));
                break;

            case PanelStroke.Scroll:
                _scroller.ScrollToVerticalOffset(_offsetFrom - (at.Y - _from.Y));
                break;

            case PanelStroke.Out when _carrying:
                _carry.Move(_scroller.PointToScreen(at));
                break;

            default:
                break;
        }
    }

    private void Up(FrameworkElement surface, TouchEventArgs up)
    {
        if (up.TouchDevice.Id != _finger)
        {
            return;
        }

        up.Handled = true;

        var at = up.GetTouchPoint(_scroller).Position;
        var carrying = _carrying;
        var tapped = _stroke is PanelStroke.Undecided && !_held ? _card : null;

        _carrying = false;
        _ = surface.ReleaseTouchCapture(up.TouchDevice);
        Reset(abandon: false);

        if (carrying)
        {
            _carry.Drop(_scroller.PointToScreen(at));
            return;
        }

        if (tapped is null)
        {
            return;
        }

        var now = Environment.TickCount64;

        if (ReferenceEquals(tapped, _lastTap)
            && now - _lastTapMs <= GetDoubleClickTime()
            && (at - _lastTapAt).Length <= TapsApart)
        {
            _lastTap = null;
            DoubleTapped?.Invoke(this, tapped);
            return;
        }

        _lastTap = tapped;
        _lastTapAt = at;
        _lastTapMs = now;
        Tapped?.Invoke(this, tapped);
    }

    private void Reset(bool abandon)
    {
        _press.Up();
        _finger = null;
        _card = null;

        if (abandon && _carrying)
        {
            _carrying = false;
            _carry.End();
        }
    }

    /// <summary>The double-click time the DM set in Windows - a double tap is the same thing with a finger.</summary>
    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    private FrameworkElement? CardOf(DependencyObject? hit)
    {
        for (var at = hit; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement element && _cards.ContainsKey(element))
            {
                return element;
            }
        }

        return null;
    }
}
