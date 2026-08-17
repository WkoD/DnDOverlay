using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DnDOverlay.Core;
using DnDOverlay.Platform.Windows;
using DnDOverlay.Rendering.Windows;
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
    /// <summary>
    /// How long the name stands when the DM asks which screen is which. A few seconds (Part 6):
    /// long enough to walk round the table and look, short enough that it is gone again before
    /// anybody wonders how to get rid of it.
    /// </summary>
    private static readonly TimeSpan ShowNameFor = TimeSpan.FromSeconds(4);

    private readonly MonitorInfo _monitor;
    private readonly bool _windowed;
    /// <summary>
    /// The drawing surface. <c>Background = null</c> rather than <c>Transparent</c>, because a null
    /// brush is hit-test free and a transparent one is not (Part 6).
    /// <para>
    /// <c>ClipToBounds</c> is set, and it is not decoration: a Canvas does not clip by default, and
    /// the background layer under <c>Cover</c> deliberately reaches past the screen - that overhang
    /// is the crop. Without this the crop would be a hope about how far a window happens to paint.
    /// </para>
    /// </summary>
    private readonly Canvas _stage = new() { Background = null, ClipToBounds = true };

    /// <summary>
    /// The progress rings, on a layer of their own above the stage. They belong to the ungoverned
    /// layer and not to the scene (Part 7), and here that separation earns its keep twice: the
    /// rings are rebuilt on every render while the pictures beneath them are kept, and a ring can
    /// never end up behind the picture it is about.
    /// </summary>
    private readonly Canvas _rings = new() { Background = null, IsHitTestVisible = false };

    /// <summary>
    /// The places on the stage, one per item, kept between renders. See <see cref="Mount"/> for why
    /// they are kept at all.
    /// </summary>
    private readonly Dictionary<ItemId, Mount> _mounts = [];

    /// <summary>The background's place, which has no item id to be keyed by.</summary>
    private Mount? _backdrop;

    private readonly TextBlock _name;
    private readonly Border _nameplate;
    private readonly DispatcherTimer _naming;

    internal OverlayWindow(MonitorInfo monitor, bool windowed)
    {
        _monitor = monitor;
        _windowed = windowed;

        _name = new TextBlock
        {
            FontSize = 64,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        _nameplate = new Border
        {
            Child = _name,
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)),
            Padding = new Thickness(48, 32, 48, 32),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,

            // Pass-through is the property this window exists for. A plate that answered the
            // hit test would swallow clicks meant for MapTool underneath it, for four seconds,
            // in the middle of a room being set up.
            IsHitTestVisible = false,
        };

        _naming = new DispatcherTimer { Interval = ShowNameFor };
        _naming.Tick += (_, _) => Hide(_nameplate);

        // A grid over the canvas rather than another item on it: the name belongs above every
        // image and is not part of the scene. Background stays null, so the layer costs no
        // hit testing (mode A, above).
        var root = new Grid { Background = null };

        root.Children.Add(_stage);
        root.Children.Add(_rings);
        root.Children.Add(_nameplate);

        Title = monitor.Screen.Label;
        Content = root;
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

        _stage.SizeChanged += (_, _) => SurfaceChanged?.Invoke();
    }

    internal ScreenId ScreenId => _monitor.Screen.ScreenId;

    /// <summary>
    /// Raised when the surface the scene is drawn on has changed size. Whoever draws has to draw
    /// again - the scene is normalised, so every coordinate on it is a fraction of exactly this
    /// surface.
    /// <para>
    /// It exists because of what its absence did (hand-run of M2b, third round): a window is drawn
    /// into the moment it is shown, and at that moment WPF has not laid it out yet, so the stage
    /// reports no size at all and the drawing falls back to the size the SCREEN reports. The two
    /// are not the same number, and the pictures therefore stood in one place until the next
    /// arrival redrew them somewhere else - which read as "switching a screen off and on moves the
    /// pictures, adding one puts them back".
    /// </para>
    /// <para>
    /// No loop: a canvas takes its size from its parent and not from what is put on it, so drawing
    /// cannot change the size that caused the drawing.
    /// </para>
    /// </summary>
    internal event Action? SurfaceChanged;

    /// <summary>
    /// Shows this screen's effective name, large, for a few seconds. Pressing again restarts the
    /// few seconds rather than queueing a second showing - the DM presses twice when they are not
    /// sure they saw it, and the answer to that is a longer look, not two.
    /// </summary>
    internal void Identify(string label)
    {
        _name.Text = label;
        _nameplate.Visibility = Visibility.Visible;

        _naming.Stop();
        _naming.Start();
    }

    private static void Hide(UIElement plate) => plate.Visibility = Visibility.Collapsed;

    protected override void OnClosed(EventArgs e)
    {
        // A running timer holds this window alive and would tick into a closed one.
        _naming.Stop();

        base.OnClosed(e);
    }

    /// <summary>
    /// Draws the scene. Everything goes through <see cref="Layout.ItemToRect"/> - the table, the
    /// thumbnail and every later preview use the same computation, which is what makes the
    /// thumbnail trustworthy (Part 1, rule 9).
    /// <para>
    /// <b>The stage is not cleared.</b> Each item keeps its place across renders and only what
    /// actually changed is touched, because an animation is a running clock and rebuilding it is
    /// not the same as leaving it alone. Measured at the table (hand-run of M2b, step 24): every
    /// change to the scene - switching the background on, renaming something - sent every animation
    /// back to its first frame, and each restart cost the half second it takes to decode the frames
    /// again. What to do with each place is decided in <see cref="PictureTransition"/>, in Core,
    /// where it can be asserted.
    /// </para>
    /// </summary>
    internal void Render(
        SceneState scene,
        ScreenContext context,
        IReadOnlyDictionary<AssetId, ImageSource> images,
        IReadOnlyDictionary<AssetId, byte[]> moving,
        IReadOnlyDictionary<AssetId, double> loading)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var width = _stage.ActualWidth > 0 ? _stage.ActualWidth : context.WidthInDip;
        var height = _stage.ActualHeight > 0 ? _stage.ActualHeight : context.HeightInDip;

        // Which pictures may move is decided over the scene, in Core, before anything is built -
        // a continuous animation on a software-rendered transparent overlay is the most expensive
        // case this application has (Part 6).
        var animating = AnimationBudget.Plan(scene);

        // The two layers are independent in all four combinations, and each switch hides its own
        // and nothing else (Part 11, step 24). Hiding is not removing: the pictures stay in the
        // scene and in the device's store, which is what makes fading them back in immediate and
        // free of a second transfer (Part 7).
        DrawBackground(scene, context, images, moving, width, height, animating.Background);

        var standing = new HashSet<ItemId>();

        if (scene.ItemsVisible)
        {
            foreach (var item in scene.Items)
            {
                if (item is not ImageItem image || !images.TryGetValue(image.AssetId, out var source))
                {
                    continue;
                }

                standing.Add(image.ItemId);

                var mount = _mounts.TryGetValue(image.ItemId, out var known) ? known : Raise(image.ItemId);

                // Depth by Z index rather than by the order things were added, so keeping a place
                // between renders does not decide what lies on top of what.
                Panel.SetZIndex(mount.Element, item.ZOrder);

                Show(
                    mount,
                    source,
                    image.AssetId,
                    moving.GetValueOrDefault(image.AssetId),
                    animating.Items.Contains(image.ItemId),
                    image.AnimationPaused);

                Lay(
                    mount,
                    Layout.ItemToRect(item, context),
                    item.RotationDeg,
                    width,
                    height,
                    image.ShowName ? image.Name : null,
                    context.ImageTextSize);
            }
        }

        foreach (var (id, mount) in _mounts.Where(pair => !standing.Contains(pair.Key)).ToList())
        {
            _stage.Children.Remove(mount.Element);
            _mounts.Remove(id);
        }

        // The rings live on their own layer above everything: they belong to the ungoverned layer,
        // not to the scene. If a picture is being fetched, its place already stands - what is
        // missing is the picture, and the ring says so where it will appear (Part 7). They are the
        // one thing rebuilt every time, because a ring is a number and not a running clock.
        _rings.Children.Clear();

        if (!scene.ItemsVisible)
        {
            return;
        }

        foreach (var item in scene.Items.OfType<ImageItem>())
        {
            if (loading.TryGetValue(item.AssetId, out var fraction))
            {
                _rings.Children.Add(Ring(Layout.ItemToRect(item, context), fraction, width, height));
            }
        }
    }

    /// <summary>Makes a new place on the stage and registers it under its item.</summary>
    private Mount Raise(ItemId item)
    {
        var mount = Raise();
        _mounts[item] = mount;

        return mount;
    }

    private Mount Raise()
    {
        var mount = new Mount();
        _stage.Children.Add(mount.Element);

        return mount;
    }

    /// <summary>
    /// The progress ring at the item's place. It is fed from <c>AssetProgress</c> and NOT from the
    /// scene stream, which is why it keeps turning while everything else is slow - under load the
    /// feedback that explains the load must not be the first thing to stop (Part 7, rank 3).
    /// </summary>
    private static Grid Ring(CoreRect rect, double fraction, double width, double height)
    {
        const double Size = 56;

        var ring = new Grid { Width = Size, Height = Size, IsHitTestVisible = false };

        ring.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 5,
            Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
        });

        // An arc rather than a second ellipse: a partial circle is what says "some of it", and a
        // ring that only changed colour would read as a state rather than as a quantity.
        var arc = new System.Windows.Shapes.Path
        {
            Stroke = Brushes.White,
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Arc(Size / 2, (Size / 2) - 4, Math.Clamp(fraction, 0, 1)),
        };

        ring.Children.Add(arc);

        Canvas.SetLeft(ring, ((rect.X + (rect.Width / 2)) * width) - (Size / 2));
        Canvas.SetTop(ring, ((rect.Y + (rect.Height / 2)) * height) - (Size / 2));

        return ring;
    }

    /// <summary>The filled part, clockwise from twelve o'clock.</summary>
    private static Geometry Arc(double centre, double radius, double fraction)
    {
        if (fraction <= 0)
        {
            return Geometry.Empty;
        }

        var angle = fraction * 2 * Math.PI;
        var start = new System.Windows.Point(centre, centre - radius);
        var end = new System.Windows.Point(
            centre + (radius * Math.Sin(angle)),
            centre - (radius * Math.Cos(angle)));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };

        figure.Segments.Add(new ArcSegment
        {
            Point = fraction >= 1 ? new System.Windows.Point(centre - 0.01, centre - radius) : end,
            Size = new System.Windows.Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = fraction > 0.5,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        return geometry;
    }

    /// <summary>
    /// The background layer, beneath everything - added first, so it is at the bottom of the canvas
    /// without needing a Z value of its own.
    /// <para>
    /// Under <c>Cover</c> the rectangle reaches past the screen on purpose: that overhang IS the
    /// crop. The stage clips, so what hangs over is simply not drawn - the alternative would be
    /// cropping the bitmap here and computing the geometry a second time (Part 6).
    /// </para>
    /// </summary>
    private void DrawBackground(
        SceneState scene,
        ScreenContext context,
        IReadOnlyDictionary<AssetId, ImageSource> images,
        IReadOnlyDictionary<AssetId, byte[]> moving,
        double width,
        double height,
        bool animate)
    {
        if (!scene.BackgroundVisible
            || scene.Background is not { } background
            || !images.TryGetValue(background.AssetId, out var source))
        {
            if (_backdrop is { } gone)
            {
                _stage.Children.Remove(gone.Element);
                _backdrop = null;
            }

            return;
        }

        _backdrop ??= Raise();

        // Beneath everything, whatever Z order the items carry. The background is a layer and not
        // an item, so it does not compete for depth with them.
        Panel.SetZIndex(_backdrop.Element, int.MinValue);

        Show(
            _backdrop,
            source,
            background.AssetId,
            moving.GetValueOrDefault(background.AssetId),
            animate,
            background.AnimationPaused);

        var rect = Layout.BackgroundRect(
            background.Meta.AspectRatio,
            background.Fit,
            background.OffsetX,
            background.OffsetY,
            context);

        Lay(
            _backdrop,
            rect,
            rotationDeg: 0,
            width,
            height,
            background.ShowName ? background.Name : null,
            context.ImageTextSize);
    }

    /// <summary>
    /// Brings one place up to date with the picture it should show. What has to happen is decided in
    /// <see cref="PictureTransition"/> - here is only the doing of it.
    /// </summary>
    private static void Show(
        Mount mount,
        ImageSource source,
        AssetId asset,
        byte[]? bytes,
        bool admitted,
        bool paused)
    {
        // A picture may only move if the budget admitted it AND the bytes to move it are here. The
        // second half is not a detail: the animated path needs the BYTES rather than the decoded
        // picture, and that is measured - handed the decoded one the library reports zero frames,
        // because the frames of a GIF are read a second time from the source and PictureDecoder has
        // let its stream go.
        var canMove = admitted && bytes is not null;

        // The identifier alone would say "nothing changed" when the sharp picture lands on top of
        // the blurred stand-in - both carry the same one. So the bitmap itself is part of the
        // question, and it is compared by identity: two decodes of one picture are two objects, and
        // the second is the one that has to go up.
        var action = PictureTransition.Next(
            mount.State, mount.Showing, asset, ReferenceEquals(mount.Applied, source), canMove, paused);

        switch (action)
        {
            case PictureAction.Start:
                AnimatedPicture.Run(mount.Picture, bytes!);
                break;

            case PictureAction.Resume:
                AnimatedPicture.Resume(mount.Picture);
                break;

            case PictureAction.Hold:
                AnimatedPicture.Hold(mount.Picture);
                break;

            case PictureAction.Freeze:
                if (source is BitmapSource still)
                {
                    AnimatedPicture.Freeze(mount.Picture, still);
                }
                else
                {
                    mount.Picture.Source = source;
                }

                break;

            default:
                break;
        }

        mount.Showing = asset;
        mount.Applied = source;
        mount.State = PictureTransition.After(mount.State, action);
    }

    /// <summary>
    /// Puts a place where it belongs and hangs its caption on it. Geometry is written on every
    /// render because it is cheap and always current; the picture inside is not touched here.
    /// </summary>
    private static void Lay(
        Mount mount,
        CoreRect rect,
        double rotationDeg,
        double width,
        double height,
        string? name,
        double textSize)
    {
        var renderedWidth = rect.Width * width;
        var renderedHeight = rect.Height * height;

        mount.Element.Width = renderedWidth;
        mount.Element.Height = renderedHeight;
        mount.Turn.Angle = rotationDeg;

        Canvas.SetLeft(mount.Element, rect.X * width);
        Canvas.SetTop(mount.Element, rect.Y * height);

        // In DIP on the screen, not in normalised coordinates - the text does not scale with the
        // picture, which is the whole reason the cascade is measured rather than computed. The size
        // comes from the SCREEN, because only the viewing distance separates a table from a
        // projector and no machine can read that off the hardware (Part 6).
        var caption = CaptionLayout.Fit(name, renderedWidth, renderedHeight, textSize);

        if (mount.Caption is { } old)
        {
            mount.Element.Children.Remove(old);
            mount.Caption = null;
        }

        if (caption.IsVisible)
        {
            mount.Caption = Label(caption, renderedWidth);
            mount.Element.Children.Add(mount.Caption);
        }
    }

    /// <summary>
    /// One picture's place on the stage, kept from one render to the next.
    /// <para>
    /// The picture and its caption sit in a single rotated container, so the caption turns WITH the
    /// picture - nobody is helped by a readable label under a figure standing on its head
    /// (<c>checks/M1.md</c>).
    /// </para>
    /// <para>
    /// It is kept, rather than built each time, because of what it carries: an animation is a
    /// running clock, and a new <c>Image</c> means a new clock at frame one. What it currently shows
    /// is remembered here so that <see cref="PictureTransition"/> can tell an unchanged picture from
    /// a replaced one.
    /// </para>
    /// </summary>
    private sealed class Mount
    {
        internal Mount()
        {
            Turn = new RotateTransform(0);

            Element = new Grid
            {
                IsHitTestVisible = false,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = Turn,
            };

            Element.Children.Add(Picture);
        }

        internal Grid Element { get; }

        internal Image Picture { get; } = new() { Stretch = Stretch.Fill };

        internal RotateTransform Turn { get; }

        internal Border? Caption { get; set; }

        internal AssetId? Showing { get; set; }

        /// <summary>
        /// The bitmap that was last put up here. Kept beside the identifier because one asset has
        /// more than one rendering over its life - the thumbnail stand-in and then the original.
        /// </summary>
        internal ImageSource? Applied { get; set; }

        internal PictureState State { get; set; } = PictureState.Nothing;
    }

    /// <summary>
    /// The name plate: inside the picture, at the bottom, on a dark gradient that fades out
    /// upwards.
    /// <para>
    /// The gradient is the price the plan already accepted for the inventory tiles and it carries
    /// unchanged here - white text on a light picture is unreadable, and this is not a second
    /// decision (<c>checks/M1.md</c>).
    /// </para>
    /// </summary>
    private static Border Label(Caption caption, double width)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 1),
            EndPoint = new System.Windows.Point(0, 0),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(0xC0, 0, 0, 0), 0),
                new GradientStop(Colors.Transparent, 1),
            ],
        };

        return new Border
        {
            Background = gradient,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(4, 8, 4, 2),
            Child = new TextBlock
            {
                Text = caption.Text,
                FontSize = CaptionLayout.DefaultTextSize,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = width,
            },
        };
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
