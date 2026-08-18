using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DnDOverlay.Core;
using DnDOverlay.Platform.Windows;
using DnDOverlay.Rendering.Windows;
using CoreManipulation = DnDOverlay.Core.Manipulation;
using CorePoint = DnDOverlay.Core.Point;
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

    private MonitorInfo _monitor;
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

    /// <summary>
    /// The items a hand is on right now, with the values the gesture has produced so far.
    /// <para>
    /// While something is held, the LOCAL gesture wins: incoming transforms for it are passed over
    /// until the fingers leave, and a second hand on the same picture is not a foreign access
    /// (Part 4, conflict rule 3). This table is what makes that decidable.
    /// </para>
    /// </summary>
    private readonly Dictionary<ItemId, Hold> _held = [];

    /// <summary>
    /// What was last drawn here, so a gesture can be answered without asking anybody: whether the
    /// screen takes gestures at all, whether this item is locked, whether a focus lies.
    /// </summary>
    private SceneState _scene = SceneState.Empty;
    private ScreenContext? _context;

    /// <summary>Where the mouse was at the last step of a drag, in stage DIP.</summary>
    private System.Windows.Point _mouseAt;

    /// <summary>
    /// When and where the last tap ended, for the double tap that turns a picture to whoever
    /// tapped. <c>0</c> means "no tap is waiting for a second one".
    /// </summary>
    private long _lastTap;
    private System.Windows.Point _lastTapDip;

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
    /// The screen changed its resolution, its scaling or its place, and this window follows it.
    /// <para>
    /// <b>Found at the table (hand-run of M3b, step 37c3), and it produced both halves of that
    /// finding.</b> A window is placed when it is created and never again, so after a resolution
    /// change it kept the old pixel bounds: pictures near the old right edge lay OUTSIDE the new
    /// screen and could not be reached, and everything on it was stretched - the scene is
    /// normalised against the screen's aspect ratio while it was being drawn onto a surface of the
    /// old shape. The hub had recomputed the items correctly; the surface they were drawn on was
    /// the wrong one.
    /// </para>
    /// <para>
    /// Resizing raises <see cref="SurfaceChanged"/> by itself, so the redraw is not asked for here.
    /// </para>
    /// </summary>
    internal void Moved(MonitorInfo monitor)
    {
        _monitor = monitor;
        Title = monitor.Screen.Label;

        Settle();
    }

    /// <summary>
    /// How this screen stands. It decides whether gestures do anything at all: on
    /// <see cref="ScreenState.Disabled"/> everything stays visible and no gesture works, and every
    /// touch gets the same short answer a locked picture gives - a player must not have to guess
    /// which of the two it was (Part 6).
    /// </summary>
    internal ScreenState State { get; set; } = ScreenState.Enabled;

    /// <summary>
    /// Whether a hand is on something here. The load path asks it before every picture: while
    /// somebody is pushing, downloads drop to one at a time (Part 1, order of precedence).
    /// </summary>
    internal bool Holding => _held.Count > 0;

    /// <summary>
    /// What one report of a running gesture carries. A record rather than four arguments, because
    /// three of them are booleans and a call site with three booleans in a row is a place to make a
    /// mistake that compiles.
    /// </summary>
    /// <param name="KnownRevision">
    /// The revision this display had for the item when the hand took hold of it - not the newest it
    /// has seen since. That is what makes "was the picture that was grabbed the current one?"
    /// answerable at the other end.
    /// </param>
    internal readonly record struct Reported(
        ItemTransform Transform,
        long KnownRevision,
        bool Grabbed,
        bool Binding);

    /// <summary>
    /// A player moved a picture. The values are LOCAL and already held at the edge; what the hub
    /// makes of them comes back as a patch.
    /// </summary>
    internal event Action<Reported>? Transformed;

    /// <summary>
    /// This item is being drawn larger than the bitmap it has. The step above is asked for once per
    /// crossing; whether there is one left is the application's question, because only it knows what
    /// the source holds (<see cref="DecodeSteps"/>).
    /// </summary>
    internal event Action<AssetId, int>? Sharpen;

    /// <summary>
    /// More pictures are ready than this pass would hang up. Whoever handles it draws again, at
    /// background priority - the point is to let input through in between (Part 1, order of
    /// precedence).
    /// </summary>
    internal event Action? MoreToShow;

    /// <summary>
    /// A player swiped a picture into the slot bar, or took one back out by touching it. Where it
    /// then lies is not reported: that follows from the list of parked pictures and this screen's
    /// park edge, and the hub works it out with the same function this window would have used.
    /// </summary>
    internal event Action<ItemId, bool>? Parked;

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
        ArgumentNullException.ThrowIfNull(context);

        _scene = scene;
        _context = context;

        // A hand on a picture that is no longer in the scene: the manipulation is ended rather than
        // frozen, which is what a scene change under a finger has to look like (Part 4, rule 4).
        foreach (var gone in _held.Keys.Where(id => scene.Items.All(item => item.ItemId != id)).ToList())
        {
            _held.Remove(gone);
        }

        var (width, height) = Surface(context);

        // One picture may be hung up in this pass, and whether anything had to wait for the next.
        var hung = false;
        var waiting = false;

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

                // <b>At most one picture is hung up per pass.</b> Twenty arriving at once would
                // otherwise all be built into the visual tree in one drawing - and that drawing is
                // the frame a hand at the table is waiting for (Part 11, the priority rule). What is
                // left over comes on the next pass, which is asked for below.
                if (Show(
                        mount,
                        source,
                        image.AssetId,
                        moving.GetValueOrDefault(image.AssetId),
                        animating.Items.Contains(image.ItemId),
                        image.AnimationPaused,
                        hung))
                {
                    hung = true;
                }
                else if (mount.Applied != source)
                {
                    waiting = true;
                }

                Ask(image, Layout.ItemToRect(item, context), context, source);

                // A held item keeps the geometry the finger gave it. Writing the scene's over it
                // would drag the picture back to where the hub last knew it, twenty times a second,
                // for as long as somebody is pushing it (Part 4, conflict rule 3).
                if (!_held.ContainsKey(image.ItemId))
                {
                    Lay(
                        mount,
                        Layout.ItemToRect(item, context),
                        item.RotationDeg,
                        width,
                        height,
                        image.ShowName ? image.Name : null,
                        context.ImageTextSize);
                }

                // The padlock is drawn either way: seeing which pictures are locked is what makes
                // "unlock all" harmless, and it has to be visible at the table as well as in the
                // thumbnail (Part 3).
                Latch(mount, item.Locked);
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
            if (!loading.TryGetValue(item.AssetId, out var fraction))
            {
                continue;
            }

            // At the place the FINGER has it, not the one the hub last knew. Measured at the table
            // (hand-run of M3b, step 37b): a picture still loading could be pushed around while its
            // ring stayed where it had first appeared - the ring is rebuilt from the scene, and
            // during a gesture the scene is deliberately the older of the two truths.
            var where = _held.TryGetValue(item.ItemId, out var hold) ? hold.Item : item;

            _rings.Children.Add(Ring(Layout.ItemToRect(where, context), fraction, width, height));
        }

        if (waiting)
        {
            MoreToShow?.Invoke();
        }
    }

    /// <summary>
    /// Asks for a sharper decode when an item is being drawn larger than the bitmap it has.
    /// <para>
    /// The comparison is against the BITMAP rather than against a note kept somewhere: what is on
    /// the screen knows its own width, and a second table over the same question is the shape that
    /// cost 2 GB in M2b.
    /// </para>
    /// </summary>
    private void Ask(ImageItem item, CoreRect rect, ScreenContext context, ImageSource source)
    {
        if (source is not BitmapSource bitmap)
        {
            return;
        }

        // In physical pixels of this screen - the unit a decode step is measured in.
        var needed = (int)Math.Round(rect.Width * context.Size.Width);

        if (needed > bitmap.PixelWidth && bitmap.PixelWidth < item.Meta.PixelWidth)
        {
            Sharpen?.Invoke(item.AssetId, needed);
        }
    }

    /// <summary>Makes a new place on the stage and registers it under its item.</summary>
    private Mount Raise(ItemId item)
    {
        var mount = Raise();
        _mounts[item] = mount;

        Handle(item, mount);

        return mount;
    }

    /// <summary>
    /// Makes one place answer hands and mice. Attached once when the place is made, because these
    /// are the handlers of an element that outlives every render (see <see cref="Mount"/>).
    /// <para>
    /// <b>The item answers the hit test and the stage does not</b>, and that pair is the whole
    /// pass-through story: a touch on empty table goes to MapTool underneath, a touch on a picture
    /// is ours (spike A, mode A). A locked picture stays hit-testable on purpose - it owes the
    /// finger an answer, and that answer is a flash rather than silence (Part 6).
    /// </para>
    /// </summary>
    private void Handle(ItemId item, Mount mount)
    {
        var element = mount.Element;

        element.IsHitTestVisible = true;
        element.IsManipulationEnabled = true;

        element.ManipulationStarting += (_, e) =>
        {
            // Measured against the stage, so a translation arrives in the units the scene is
            // normalised in - and so two fingers on two pictures stay two manipulations.
            e.ManipulationContainer = _stage;
            e.Mode = ManipulationModes.All;
            e.Handled = true;
        };

        element.ManipulationStarted += (_, e) =>
        {
            if (!Take(item, e.ManipulationOrigin))
            {
                e.Cancel();
            }

            e.Handled = true;
        };

        element.ManipulationDelta += (_, e) => Move(item, mount, e);
        element.ManipulationInertiaStarting += (_, e) => Fling(item, e);
        element.ManipulationCompleted += (_, e) => LetGo(item, mount, e);

        element.MouseLeftButtonDown += (_, e) => MouseTake(item, element, e);
        element.MouseMove += (_, e) => MouseDrag(item, mount, e);
        element.MouseLeftButtonUp += (_, e) => MouseLetGo(item, mount, element, e);
        element.MouseWheel += (_, e) => Wheel(item, mount, e);
    }

    /// <summary>
    /// Takes hold of an item, or refuses to. A parked picture is taken OUT of the bar by being
    /// touched, which is the only way back for the players (Part 6).
    /// </summary>
    private bool Take(ItemId item, System.Windows.Point origin)
    {
        if (_context is not { } context
            || _scene.Items.FirstOrDefault(one => one.ItemId == item) is not { } current)
        {
            return false;
        }

        if (!CoreManipulation.AcceptsGestures(_scene, current, State))
        {
            // The one answer for all three reasons - padlock, disabled screen, focus lying. A
            // player who gets nothing at all presses harder and decides the table is broken.
            Refuse(item);

            return false;
        }

        if (current.Parked)
        {
            Parked?.Invoke(item, false);
        }

        var (width, height) = Surface(context);

        _held[item] = new Hold(current)
        {
            TapDip = origin,
            Tap = new CorePoint(width <= 0 ? 0 : origin.X / width, height <= 0 ? 0 : origin.Y / height),
        };

        // Grabbed: what is taken hold of comes to the front, locally at once and bindingly from the
        // hub right afterwards (Part 3).
        Report(item, grabbed: true, binding: false);

        return true;
    }

    /// <summary>One step of a hand on a picture, inertial or not.</summary>
    private void Move(ItemId item, Mount mount, ManipulationDeltaEventArgs e)
    {
        e.Handled = true;

        if (_context is not { } context || !_held.TryGetValue(item, out var hold))
        {
            // The picture went away under the finger - a scene change while somebody was holding
            // it. Ending the manipulation is the difference between a picture that disappears and
            // a finger frozen onto one that no longer exists (Part 4, conflict rule 4).
            e.Complete();

            return;
        }

        if (hold.Parked)
        {
            // The swipe has already gone into the bar. A residual inertial step would report a
            // position the hub would take for the newest truth.
            e.Complete();

            return;
        }

        var (width, height) = Surface(context);
        var delta = e.DeltaManipulation;

        // Friction that rises towards the edge, and only while gliding: under the finger the clamp
        // alone decides, or the picture would feel sticky in the hand (Part 6).
        var damping = e.IsInertial ? CoreManipulation.EdgeResistance(hold.Item, context) : 1;

        var step = new GestureStep(
            width <= 0 ? 0 : delta.Translation.X / width * damping,
            height <= 0 ? 0 : delta.Translation.Y / height * damping,
            (delta.Scale.X + delta.Scale.Y) / 2,
            delta.Rotation,
            new CorePoint(
                width <= 0 ? 0 : e.ManipulationOrigin.X / width,
                height <= 0 ? 0 : e.ManipulationOrigin.Y / height));

        var (moved, turning) = CoreManipulation.Step(hold.Item, hold.Turning, step, context);

        hold.Item = moved;
        hold.Turning = turning;
        hold.Moved += Math.Abs(delta.Translation.X) + Math.Abs(delta.Translation.Y);

        Place(mount, moved, context);
        Report(item, grabbed: false, binding: false);

        if (e.IsInertial && damping <= 0)
        {
            // At the point the clamp would take over there is nothing left to glide into.
            e.Complete();
        }
    }

    /// <summary>
    /// The fingers have left and the picture would now glide. <b>This is where the park decision is
    /// read</b>, because it is the moment the swipe had its speed - after the glide that speed is
    /// nearly zero (Part 6).
    /// </summary>
    private void Fling(ItemId item, ManipulationInertiaStartingEventArgs e)
    {
        e.Handled = true;

        if (_context is not { } context || !_held.TryGetValue(item, out var hold))
        {
            return;
        }

        // WPF measures in DIP per millisecond, the rule is written in DIP per second.
        var velocity = e.InitialVelocities.LinearVelocity;

        if (CoreManipulation.ShouldPark(hold.Item, velocity.X * 1000, velocity.Y * 1000, context))
        {
            // Marked BEFORE the message goes out: everything this gesture does from here on has to
            // stay silent, or the transform that follows undoes the park (see Hold.Parked).
            hold.Parked = true;

            Parked?.Invoke(item, true);
            Still(e);

            return;
        }

        if (!context.Inertia)
        {
            Still(e);

            return;
        }

        // Proposal until measured (hand-run of M3b, step 18a): a fling of about 1 DIP/ms comes to
        // rest in roughly half a second. Scaling and rotation keep their own behaviour - it is the
        // pushing over distance that a table lying flat asks for (Part 6).
        e.TranslationBehavior.DesiredDeceleration = 0.0025;
    }

    /// <summary>
    /// No glide at all: the picture stays where the fingers left it.
    /// <para>
    /// Through the DISPLACEMENT rather than a <c>Complete</c> - this event has none, and a huge
    /// deceleration would be a number chosen to look like zero. Nought displacement says what is
    /// meant, and WPF ends the manipulation by itself once there is nowhere left to go.
    /// </para>
    /// </summary>
    private static void Still(ManipulationInertiaStartingEventArgs e) =>
        e.TranslationBehavior = new InertiaTranslationBehavior { DesiredDisplacement = 0 };

    /// <summary>
    /// The gesture is over: the angle settles onto a quarter turn if it is close enough - never
    /// before, because a picture that clicks into place under the finger feels broken - and the
    /// binding report goes out.
    /// </summary>
    private void LetGo(ItemId item, Mount mount, ManipulationCompletedEventArgs e)
    {
        e.Handled = true;

        if (_context is not { } context || !_held.TryGetValue(item, out var hold))
        {
            return;
        }

        if (hold.Parked)
        {
            // Where a parked picture lies is the bar's business, and the bar is worked out at both
            // ends from the LIST. A binding transform here would be this gesture answering a
            // question it does not get to answer - and it would win, because it arrives second.
            _held.Remove(item);

            return;
        }

        if (Tapped(hold, e.TotalManipulation.Translation))
        {
            // Two quick taps turn the picture to whoever tapped - the biggest comfort gain at a
            // table lying flat, and the reason the snap angles are the quarter turns (Part 6).
            hold.Item = CoreManipulation.HoldAtEdge(
                hold.Item with { RotationDeg = CoreManipulation.TurnToMe(hold.Tap, context) },
                context);
        }
        else
        {
            hold.Item = CoreManipulation.Settle(hold.Item, context);
        }

        Place(mount, hold.Item, context);
        Report(item, grabbed: false, binding: true);

        _held.Remove(item);
    }

    /// <summary>
    /// Whether this manipulation was the second of two quick taps on nearly the same spot.
    /// <para>
    /// Decided at the END of a manipulation rather than on touch-down, so it never has to fight
    /// WPF's input promotion: a tap IS a manipulation, one that moved almost nothing.
    /// <c>Environment.TickCount64</c> because it only ever measures distances between two of its own
    /// readings - a wall clock stepping backwards mid-evening would make a double tap unreachable.
    /// </para>
    /// </summary>
    private bool Tapped(Hold hold, Vector total)
    {
        const double TapDip = 12;
        const long TapMs = 300;
        const double NearDip = 40;
        const long TwiceMs = 400;

        var now = Environment.TickCount64;

        if (hold.Moved > TapDip || Math.Abs(total.X) + Math.Abs(total.Y) > TapDip || now - hold.Began > TapMs)
        {
            return false;
        }

        var twice = now - _lastTap <= TwiceMs
            && Math.Abs(hold.TapDip.X - _lastTapDip.X) <= NearDip
            && Math.Abs(hold.TapDip.Y - _lastTapDip.Y) <= NearDip;

        // A third tap does not turn it again: the pair is spent, or holding a finger down and
        // tapping would spin the picture.
        _lastTap = twice ? 0 : now;
        _lastTapDip = hold.TapDip;

        return twice;
    }

    /// <summary>
    /// A short flash, the same for all three reasons a gesture is suppressed. It is the answer the
    /// finger gets, and it is the reason nobody presses harder (Part 3, Part 6).
    /// </summary>
    private void Refuse(ItemId item)
    {
        if (!_mounts.TryGetValue(item, out var mount))
        {
            return;
        }

        var flash = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };

        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));

        mount.Element.BeginAnimation(OpacityProperty, flash);
    }

    /// <summary>
    /// The mouse, which is not optional: not every display PC has touch, and the three grips are
    /// the same ones the thumbnail will offer in M4 - left drag moves, the wheel zooms about the
    /// cursor, Ctrl+drag turns (Part 6).
    /// <para>
    /// <b>The right button stays unassigned</b>, although it is free. A right drag to rotate would
    /// be a grip that exists on one of the two surfaces only, and Ctrl+drag lies next to the left
    /// hand anyway.
    /// </para>
    /// </summary>
    private void MouseTake(ItemId item, Grid element, MouseButtonEventArgs e)
    {
        if (!Take(item, e.GetPosition(_stage)))
        {
            return;
        }

        _mouseAt = e.GetPosition(_stage);
        element.CaptureMouse();
        e.Handled = true;
    }

    private void MouseDrag(ItemId item, Mount mount, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || _context is not { } context
            || !_held.TryGetValue(item, out var hold))
        {
            return;
        }

        var now = e.GetPosition(_stage);
        var (width, height) = Surface(context);

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? new GestureStep(0, 0, 1, Swept(hold.Item, _mouseAt, now, context), Centre(hold.Item))
            : new GestureStep(
                width <= 0 ? 0 : (now.X - _mouseAt.X) / width,
                height <= 0 ? 0 : (now.Y - _mouseAt.Y) / height,
                1,
                0,
                Centre(hold.Item));

        var (moved, turning) = CoreManipulation.Step(hold.Item, hold.Turning, step, context);

        hold.Item = moved;
        hold.Turning = turning;
        hold.Moved += Math.Abs(now.X - _mouseAt.X) + Math.Abs(now.Y - _mouseAt.Y);
        _mouseAt = now;

        Place(mount, moved, context);
        Report(item, grabbed: false, binding: false);

        e.Handled = true;
    }

    private void MouseLetGo(ItemId item, Mount mount, Grid element, MouseButtonEventArgs e)
    {
        element.ReleaseMouseCapture();

        if (_context is not { } context || !_held.TryGetValue(item, out var hold))
        {
            return;
        }

        hold.Item = CoreManipulation.Settle(hold.Item, context);

        Place(mount, hold.Item, context);
        Report(item, grabbed: false, binding: true);

        _held.Remove(item);
        e.Handled = true;
    }

    /// <summary>
    /// The wheel zooms about the CURSOR, not about the picture's centre - the point under the
    /// pointer stays under the pointer. Which way is larger is a per-screen setting, because Java
    /// had it inverted and it is the one thing about a wheel everybody has an opinion on (Part 6).
    /// </summary>
    private void Wheel(ItemId item, Mount mount, MouseWheelEventArgs e)
    {
        if (_context is not { } context)
        {
            return;
        }

        if (_scene.Items.FirstOrDefault(one => one.ItemId == item) is not { } current)
        {
            return;
        }

        // A wheel notch is not a hold: it has no beginning and no end, so it reports bindingly at
        // once. Taking hold for a single notch would mean a grab and a release per click of the
        // wheel, and the picture would climb to the front on every one of them.
        if (!_held.TryGetValue(item, out var hold))
        {
            if (!CoreManipulation.AcceptsGestures(_scene, current, State))
            {
                Refuse(item);

                return;
            }

            hold = new Hold(current);
        }

        const double Notch = 1.1;

        var factor = e.Delta > 0 == context.ScrollUpZoomsIn ? Notch : 1 / Notch;
        var (width, height) = Surface(context);
        var at = e.GetPosition(_stage);

        var (moved, turning) = CoreManipulation.Step(
            hold.Item,
            hold.Turning,
            new GestureStep(
                0,
                0,
                factor,
                0,
                new CorePoint(width <= 0 ? 0 : at.X / width, height <= 0 ? 0 : at.Y / height)),
            context);

        hold.Item = moved;
        hold.Turning = turning;

        Place(mount, moved, context);

        _held[item] = hold;
        Report(item, grabbed: false, binding: true);

        if (!Mouse.Captured?.Equals(mount.Element) ?? true)
        {
            // Nobody is dragging, so the hold existed only for this notch.
            _held.Remove(item);
        }

        e.Handled = true;
    }

    /// <summary>
    /// The angle the mouse swept around the item's centre between two positions. A mouse has one
    /// point, so there is no pinch to take an angle from - the centre is the only pivot that makes
    /// a drag read as a rotation.
    /// </summary>
    private double Swept(SceneItem item, System.Windows.Point from, System.Windows.Point to, ScreenContext context)
    {
        var (width, height) = Surface(context);

        var centreX = item.CenterX * width;
        var centreY = item.CenterY * height;

        var before = Math.Atan2(from.Y - centreY, from.X - centreX);
        var after = Math.Atan2(to.Y - centreY, to.X - centreX);

        return (after - before) * 180 / Math.PI;
    }

    private static CorePoint Centre(SceneItem item) => new(item.CenterX, item.CenterY);

    /// <summary>
    /// The surface the scene is normalised against. The stage's own size wherever it has one - the
    /// screen's reported size is the fallback for the moment before WPF has laid the window out,
    /// and the two are not the same number (see <see cref="SurfaceChanged"/>).
    /// </summary>
    private (double Width, double Height) Surface(ScreenContext context) =>
        (_stage.ActualWidth > 0 ? _stage.ActualWidth : context.WidthInDip,
         _stage.ActualHeight > 0 ? _stage.ActualHeight : context.HeightInDip);

    /// <summary>
    /// Puts a held item where the gesture has it, without going through a full render. That is the
    /// point of holding it locally: a redraw of the whole scene per delta would spend the frame
    /// budget on the twenty pictures that did not move (Part 1, order of precedence).
    /// </summary>
    private void Place(Mount mount, SceneItem item, ScreenContext context)
    {
        var (width, height) = Surface(context);

        Position(mount, Layout.ItemToRect(item, context), item.RotationDeg, width, height);
    }

    /// <summary>Hands the current local values of a held item outwards.</summary>
    private void Report(ItemId item, bool grabbed, bool binding)
    {
        if (!_held.TryGetValue(item, out var hold))
        {
            return;
        }

        Transformed?.Invoke(new Reported(
            new ItemTransform(
                item,
                hold.Item.CenterX,
                hold.Item.CenterY,
                hold.Item.Scale,
                hold.Item.RotationDeg),
            hold.Grabbed,
            grabbed,
            binding));
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
    /// <returns>
    /// Whether a picture was actually built into the tree here - the cost the one-per-pass budget
    /// is about. Switching an animation on or holding it still is not that cost.
    /// </returns>
    private static bool Show(
        Mount mount,
        ImageSource source,
        AssetId asset,
        byte[]? bytes,
        bool admitted,
        bool paused,
        bool spent = false)
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

        // The budget is spent and this would put a picture up: leave the place as it is and let the
        // next pass do it. Everything cheaper still happens.
        if (spent && PictureTransition.Costs(action))
        {
            return false;
        }

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

        return PictureTransition.Costs(action);
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
        var (renderedWidth, renderedHeight) = Position(mount, rect, rotationDeg, width, height);

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
    /// Lights a picture up briefly because it has just arrived. Which pictures those are is decided
    /// in <see cref="Arrival"/>, over the patch - here is only the flash.
    /// <para>
    /// A pane over the picture rather than its opacity: opacity cannot go above one, so brightening
    /// would mean dimming everything else first. The pane fades from bright to nothing and is gone
    /// afterwards, which is what <c>FillBehavior.Stop</c> is for - a highlight that stayed would be
    /// a picture that arrived for ever.
    /// </para>
    /// </summary>
    internal void Flash(ItemId item)
    {
        if (_context is not { } context
            || context.ArrivalHighlightSeconds <= 0
            || !_mounts.TryGetValue(item, out var mount))
        {
            return;
        }

        var pane = new System.Windows.Shapes.Rectangle
        {
            Fill = Brushes.White,
            Opacity = 0,
            IsHitTestVisible = false,
        };

        mount.Element.Children.Add(pane);

        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };

        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(
            0,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(context.ArrivalHighlightSeconds))));

        // Taken off again when it has finished, or every arrival of the evening would leave a
        // transparent pane on the picture and the table would slowly fill up with them.
        fade.Completed += (_, _) => mount.Element.Children.Remove(pane);

        pane.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Hangs the padlock on a place, or takes it off again.
    /// <para>
    /// <b>It is the reason "unlock all" needs no undo</b> (Part 3): whoever can see which five
    /// pictures were locked can tap them back in seconds, so the sweep costs effort rather than
    /// anything lost. Without the sign it would be a memory game.
    /// </para>
    /// <para>
    /// Drawn rather than written as a glyph. A character from an icon font would be one font
    /// dependency between a locked picture and a box on the table, and this is the sign a player
    /// looks for when a picture will not move.
    /// </para>
    /// </summary>
    private static void Latch(Mount mount, bool locked)
    {
        if (locked == (mount.Lock is not null))
        {
            return;
        }

        if (mount.Lock is { } hanging)
        {
            mount.Element.Children.Remove(hanging);
            mount.Lock = null;

            return;
        }

        var shackle = new System.Windows.Shapes.Path
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Data = Geometry.Parse("M 0,7 A 4,4 0 0 1 8,7"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var body = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(2),
            Width = 14,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var lock_ = new Grid { Width = 14, Height = 18 };

        lock_.Children.Add(shackle);
        lock_.Children.Add(body);

        // On a dark plate, for the same reason the caption sits on a gradient: white on a light
        // picture is invisible, and a sign that is only sometimes readable is not a sign.
        mount.Lock = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = lock_,
        };

        mount.Element.Children.Add(mount.Lock);
    }

    /// <summary>
    /// Writes one place's geometry, and hands back the size it came to in DIP - the caption is
    /// measured against that, and a gesture needs none of it.
    /// </summary>
    private static (double Width, double Height) Position(
        Mount mount,
        CoreRect rect,
        double rotationDeg,
        double width,
        double height)
    {
        var renderedWidth = rect.Width * width;
        var renderedHeight = rect.Height * height;

        mount.Element.Width = renderedWidth;
        mount.Element.Height = renderedHeight;
        mount.Turn.Angle = rotationDeg;

        Canvas.SetLeft(mount.Element, rect.X * width);
        Canvas.SetTop(mount.Element, rect.Y * height);

        return (renderedWidth, renderedHeight);
    }

    /// <summary>
    /// One item while a hand is on it: the values the gesture has produced so far, plus what the
    /// tap detection needs.
    /// <para>
    /// The values live here rather than in the scene because the scene is the HUB's, and during a
    /// gesture the two disagree on purpose - the finger is ahead of the wire (Part 4, conflict
    /// rule 2). They become the scene's the moment the binding report comes back as a patch.
    /// </para>
    /// </summary>
    private sealed class Hold(SceneItem item)
    {
        /// <summary>The live local values.</summary>
        internal SceneItem Item { get; set; } = item;

        internal Turning Turning { get; set; } = Turning.Beginning;

        /// <summary>How far the hand has travelled in DIP - a tap is a gesture that barely moved.</summary>
        internal double Moved { get; set; }

        /// <summary>
        /// This gesture ended in the slot bar. From then on it reports NOTHING more.
        /// <para>
        /// <b>Found at the table, and it made parking look broken.</b> The swipe sent
        /// <c>ItemParked</c>, WPF then raised <c>ManipulationCompleted</c> as it does for every
        /// gesture, and the binding <c>ItemTransformed</c> that followed carried the position the
        /// finger had let go at. The hub applied both in order: into the slot, and straight back
        /// out of it. The item was flagged as parked and lay where it had been dropped.
        /// </para>
        /// </summary>
        internal bool Parked { get; set; }

        internal long Began { get; } = Environment.TickCount64;

        /// <summary>The revision this item carried when the hand took hold of it.</summary>
        internal long Grabbed { get; } = item.Revision;

        /// <summary>Where it started, normalised, for "turn to me" - the edge nearest THAT point.</summary>
        internal CorePoint Tap { get; init; }

        internal System.Windows.Point TapDip { get; init; }
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

        /// <summary>The padlock, while this item carries one.</summary>
        internal UIElement? Lock { get; set; }

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
