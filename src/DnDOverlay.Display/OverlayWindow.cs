using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DnDOverlay.Core;
using DnDOverlay.Platform.Windows;
using CoreRect = DnDOverlay.Core.Rect;

namespace DnDOverlay.Display;

/// <summary>
/// One full-screen overlay on one screen: borderless, always on top, per-pixel transparent, and
/// invisible to hit testing wherever nothing is drawn.
/// <para>
/// Pass-through is mode A from spike A: hit-test-FREE surfaces, meaning
/// <c>Background = null</c> rather than <c>Transparent</c>. That carries for mouse and for touch
/// alike and needs no interop at all; the two interop routes stay documented reserve and nothing
/// depends on them (<c>docs/architecture.md</c>).
/// </para>
/// <para>
/// A <c>Transparent</c> brush is the trap here and looks identical on screen: it takes part in
/// hit testing and swallows every click meant for whatever runs underneath.
/// </para>
/// </summary>
internal sealed class OverlayWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly bool _windowed;
    private readonly Canvas _stage = new() { Background = null };

    internal OverlayWindow(MonitorInfo monitor, bool windowed)
    {
        _monitor = monitor;
        _windowed = windowed;

        Title = monitor.Screen.Label;
        Content = _stage;
        Background = null;
        ShowInTaskbar = windowed;

        if (windowed)
        {
            // The windowed mode is a display parameter per screen, not a development crutch
            // (Part 6) - a TV on the wall has nothing underneath it that would need an overlay.
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Width = monitor.Width / 2d;
            Height = monitor.Height / 2d;
            Background = Brushes.Black;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
        }

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    internal ScreenId ScreenId => _monitor.Screen.ScreenId;

    /// <summary>
    /// Draws the scene. Everything goes through <see cref="Layout.ItemToRect"/> - the table, the
    /// thumbnail and every later preview use the same computation, which is what makes the
    /// thumbnail trustworthy (Part 1, rule 9).
    /// </summary>
    internal void Render(
        SceneState scene,
        ScreenContext context,
        IReadOnlyDictionary<AssetId, ImageSource> images)
    {
        _stage.Children.Clear();

        if (!scene.ItemsVisible)
        {
            return;
        }

        var width = _stage.ActualWidth > 0 ? _stage.ActualWidth : context.WidthInDip;
        var height = _stage.ActualHeight > 0 ? _stage.ActualHeight : context.HeightInDip;

        foreach (var item in scene.Items.OrderBy(item => item.ZOrder))
        {
            if (item is not ImageItem image || !images.TryGetValue(image.AssetId, out var source))
            {
                continue;
            }

            var rect = Layout.ItemToRect(item, context);

            _stage.Children.Add(Place(source, rect, item.RotationDeg, width, height));
        }
    }

    private static Image Place(ImageSource source, CoreRect rect, double rotationDeg, double width, double height)
    {
        var element = new Image
        {
            Source = source,
            Width = rect.Width * width,
            Height = rect.Height * height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new RotateTransform(rotationDeg),
        };

        Canvas.SetLeft(element, rect.X * width);
        Canvas.SetTop(element, rect.Y * height);

        return element;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_windowed)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;

        // Out of the alt-tab list and never taking the foreground away from what runs beneath.
        var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle);
        Native.SetWindowLongPtr(
            handle,
            Native.GwlExStyle,
            style | Native.WsExToolWindow | Native.WsExNoActivate);

        Settle();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_windowed)
        {
            return;
        }

        // The trap spike A turned up, and it costs an afternoon to find: setting the size once
        // is not enough. Moving a window onto a monitor with a different scaling makes Windows
        // send WM_DPICHANGED afterwards, and WPF rescales the size that was just set - the
        // overlay ends up 125 % or 80 % of the screen and looks like a positioning bug.
        // Re-applying once the window has settled is what fixes it, and it has to happen at
        // Loaded priority so it runs AFTER the DPI change has been processed.
        Dispatcher.BeginInvoke(Settle, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Puts the window exactly over its monitor, in physical pixels.</summary>
    private void Settle()
    {
        var handle = new WindowInteropHelper(this).Handle;

        if (handle == 0)
        {
            return;
        }

        var (x, y, width, height) = _monitor.Bounds;

        Native.SetWindowPos(
            handle,
            insertAfter: 0,
            x,
            y,
            width,
            height,
            Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpNoOwnerZOrder);
    }
}
