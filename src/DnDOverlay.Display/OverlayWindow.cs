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

    /// <summary>How wide the progress ring is at most, in DIP.</summary>
    private const double RingSize = 56;

    /// <summary>Below this it says nothing worth the pixels, so it is left off.</summary>
    private const double RingFloor = 16;

    /// <summary>
    /// How far above the scene a held picture is drawn. Far enough that no depth the hub hands out
    /// can reach it, and an offset rather than a single top value so that two hands on two pictures
    /// keep their order relative to each other.
    /// </summary>
    private const int HeldAbove = 1 << 20;

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
    /// The one hand that is on the fan, while it is. There is at most one: the fan is a single
    /// strip, and two fingers on it would be two answers to the same question.
    /// </summary>
    private Fanning? _fan;

    /// <summary>
    /// What was last drawn here, so a gesture can be answered without asking anybody: whether the
    /// screen takes gestures at all, whether this item is locked, whether a focus lies.
    /// </summary>
    private SceneState _scene = SceneState.Empty;
    private ScreenContext? _context;

    /// <summary>How many arrival highlights are running right now - they are animations too.</summary>
    private int _flashes;

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

    /// <summary>
    /// Every touch this window has seen go down and not come up, by the identity it is reported
    /// under. It is the second source the touch events cannot give: whether the system still knows
    /// the finger.
    /// </summary>
    private readonly Dictionary<long, TouchDevice> _down = [];

    /// <summary>
    /// Sweeps <see cref="_down"/> for touches the system has let go of without telling us.
    /// <para>
    /// Once a second and at the dispatcher's ordinary background priority - it is housekeeping and
    /// must never compete with a finger, which is the whole reason the reporting itself was kept
    /// off this thread. A second of a ghost costs nothing; ten a second for ten minutes is what it
    /// replaces.
    /// </para>
    /// </summary>
    private readonly DispatcherTimer _sweep;

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

        _sweep = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sweep.Tick += (_, _) => Sweep();
        _sweep.Start();

        // A grid over the canvas rather than another item on it: the name belongs above every
        // image and is not part of the scene. Background stays null, so the layer costs no
        // hit testing (mode A, above).
        var root = new Grid { Background = null };

        root.Children.Add(_stage);
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

        // PREVIEW, and it has to be. A touch that lands on a picture is turned into a manipulation
        // and never surfaces as a bubbling touch event again, so the bubbling half would see only
        // the fingers that hit nothing - which is the opposite of what Part 4 asks for. Tunnelling
        // from the window catches all of them, on a picture or on the bare table, and it is
        // deliberately passive: nothing here marks an event handled, so the gestures underneath are
        // untouched (Part 4, "am Tisch aendert sich nichts").
        PreviewTouchDown += (_, e) => Noted(e, lifted: false);
        PreviewTouchMove += (_, e) => Noted(e, lifted: false);
        PreviewTouchUp += (_, e) => Noted(e, lifted: true);

        _stage.SizeChanged += (_, _) =>
        {
            // The surface and the screen it is supposed to be, side by side. A picture is stretched
            // exactly when these two disagree in SHAPE - the scene is normalised against the
            // screen's aspect ratio and drawn onto this surface - and the second hand-run reported
            // stretching that nothing in the log could explain (37c3). One line, and the next run
            // says which of the two is wrong.
            Surveyed?.Invoke(_stage.ActualWidth, _stage.ActualHeight, _monitor);

            SurfaceChanged?.Invoke();
        };
    }

    internal ScreenId ScreenId => _monitor.Screen.ScreenId;

    /// <summary>
    /// Every finger on this screen since anybody last asked. Filled from the UI thread, drained
    /// from a timer, and read at the display by nobody: the trails exist for the thumbnail in the
    /// control (Part 4, Part 7).
    /// </summary>
    internal TouchLog Fingers { get; } = new(TimeProvider.System);

    /// <summary>
    /// Writes one touch into the log, as a fraction of the surface - the same normalisation the
    /// scene uses, so the control can lay one over the other without knowing this screen's size
    /// (Part 3).
    /// </summary>
    private void Noted(TouchEventArgs e, bool lifted)
    {
        var width = _stage.ActualWidth;
        var height = _stage.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var at = e.GetTouchPoint(_stage).Position;
        var x = at.X / width;
        var y = at.Y / height;

        if (lifted)
        {
            _ = _down.Remove(e.TouchDevice.Id);

            Fingers.Lifted(e.TouchDevice.Id, x, y);
        }
        else
        {
            _down[e.TouchDevice.Id] = e.TouchDevice;

            Fingers.Moved(e.TouchDevice.Id, x, y);
        }
    }

    /// <summary>
    /// Forgets every touch the system no longer has, and tells the log so.
    /// <para>
    /// <b>Found in the log of the first gesture run, and nowhere else.</b> Something lay on the
    /// screen, its lift never arrived, and the resting-finger rule reported the spot ten times a
    /// second for over ten minutes - the count of reports looked healthy, and only the points per
    /// report gave it away. Resting and stuck cannot be told apart from the events, because Windows
    /// raises none for either; they can be told apart by asking whether the touch still exists.
    /// </para>
    /// <para>
    /// The identity is compared as well as the flag: touch identities are reused, so an entry whose
    /// device now reports a different one is a finger that ended without being seen to.
    /// </para>
    /// </summary>
    private void Sweep()
    {
        foreach (var (touch, device) in _down.ToList())
        {
            if (device.IsActive && device.Id == touch)
            {
                continue;
            }

            _ = _down.Remove(touch);

            Fingers.Vanished(touch);
        }
    }

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
    /// The surface changed size: its width and height in DIP, and the monitor it is meant to cover.
    /// </summary>
    internal event Action<double, double, MonitorInfo>? Surveyed;

    /// <summary>
    /// More pictures are ready than this pass would hang up. Whoever handles it draws again, at
    /// background priority - the point is to let input through in between (Part 1, order of
    /// precedence).
    /// </summary>
    internal event Action? MoreToShow;

    /// <summary>
    /// How late one movement of the hand was handled, in milliseconds - the difference between the
    /// moment the system stamped the event and the moment this window got to it.
    /// <para>
    /// <b>Measured on the finger's own events rather than on a stand-in.</b> A timer of ours says
    /// what the dispatcher queue costs in general; only the event itself says what the hand waited
    /// for, and the two came apart at the table: the queue was 300 ms behind while a whole load's
    /// worth of movement replayed at the end (M3b, fourth Pro 4 run).
    /// </para>
    /// </summary>
    internal event Action<int>? HandWaited;

    /// <summary>
    /// A gesture ended without anything to report, and the scene now says something the glass does
    /// not show. Whoever draws has to draw again.
    /// <para>
    /// It exists for the park: every other way a gesture ends sends a binding transform, and the
    /// application draws where it settles that. A park sends <c>ItemParked</c> instead and answers
    /// no question about position at all, so nothing downstream was left to trigger a drawing.
    /// </para>
    /// </summary>
    internal event Action? Settled;

    /// <summary>
    /// What a release towards the park edge measured: speed, distance, pressure, outcome. Reported
    /// outwards rather than written here, like every other number this window produces - the window
    /// draws, the application logs.
    /// </summary>
    internal event Action<long, long, long, string>? ParkMeasured;

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
        _sweep.Stop();

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
        if (_fan is { } fanning && scene.Items.All(item => item.ItemId != fanning.Card))
        {
            // The card went away under the hand - the same case as a held picture below, and the
            // same answer: the gesture ends rather than pointing at nothing (conflict rule 4).
            _fan = null;
        }

        foreach (var gone in _held.Keys.Where(id => scene.Items.All(item => item.ItemId != id)).ToList())
        {
            _held.Remove(gone);
        }

        var (width, height) = Surface(context);

        // One picture may be hung up in this pass - <b>but only while a hand is on the table</b>.
        //
        // Part 11 puts both halves of the priority rule in one sentence: "while a manipulation is
        // running, the number of parallel decodes drops to one and rises again afterwards; finished
        // pictures are hung up staggered, at most one per render pass". Both clauses hang on the
        // same condition, and applying the second one unconditionally is a misreading that was
        // measured at the table: 700 arriving pictures took a visibly long time to appear, and the
        // pace was mine. Nobody is helped by staggering when nobody is touching anything.
        var staggering = Holding;
        var hung = false;
        var waiting = false;

        // <b>Beyond the item budget the rings are what stops the table.</b> Measured in the second
        // hand-run: with 722 pictures loading at once the UI thread stood still for up to 13
        // seconds, and every one of those passes was building 722 fresh rings - a Grid, an ellipse
        // and an arc geometry each, four times a second. Part 6 puts thirty items on a 1080p screen;
        // past that the individual ring says nothing anybody can read anyway, and the feedback that
        // something is happening is the pictures arriving one after another.
        var ringing = scene.ItemsVisible && loading.Count <= AnimationBudget.ItemsPerScreen;

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
                // A held picture is drawn above everything, and keeps its own depth relative to
                // anything else being held. It is a local statement about NOW, not a change to the
                // scene: the hand is on it, so it belongs on top, and the moment the hand leaves it
                // falls back to whatever the hub says.
                //
                // Found at the table (hand-run of M3, step 17): a picture arriving while somebody
                // was pushing another one landed on top of it - the hub hands every new item the
                // next depth up, and a hand at the table had no way to outrank an arrival.
                //
                // A parked picture is not in the scene's depth order at all: the fan lies ABOVE
                // the whole table, because the way to get a picture back must never be covered by
                // the table it was tidied off (Parking.Depth).
                Panel.SetZIndex(
                    mount.Element,
                    _held.ContainsKey(image.ItemId) || _fan?.Card == image.ItemId
                        ? HeldAbove + item.ZOrder
                        : Parking.Depth(scene, item));

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
                        spent: staggering && hung))
                {
                    hung = true;
                }
                else if (mount.Applied != source)
                {
                    waiting = true;
                }

                Ask(image, Layout.ItemToRect(item, context), context, source);

                // Only a card lying IN the fan is cut. One that is being looked at or carried is a
                // picture like any other, and unfolding is the whole point of the peek.
                Trim(
                    mount,
                    item.Parked && !_held.ContainsKey(image.ItemId) && _fan?.Card != image.ItemId
                        ? Parking.CutOf(item, context)
                        : Parking.Cut.Whole,
                    context);

                // A held item keeps the geometry the finger gave it. Writing the scene's over it
                // would drag the picture back to where the hub last knew it, twenty times a second,
                // for as long as somebody is pushing it (Part 4, conflict rule 3).
                if (!_held.ContainsKey(image.ItemId) && _fan?.Card != image.ItemId)
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

                // The ring rides ON the picture, like the caption - see Turning for why it took a
                // hand-run to get there.
                Turning(mount, ringing && loading.TryGetValue(image.AssetId, out var coming) ? coming : -1);
            }
        }

        foreach (var (id, mount) in _mounts.Where(pair => !standing.Contains(pair.Key)).ToList())
        {
            _stage.Children.Remove(mount.Element);
            _mounts.Remove(id);
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
    /// Takes hold of an item, or refuses to.
    /// <para>
    /// <b>A touch on the fan does NOT take the picture out</b> (Part 6, rebuilt at the end of M3).
    /// It picks a card and shows it, and only a movement away from the edge takes that card onto
    /// the table. Which card is decided by the POINT and not by the element that answered the hit
    /// test: the newest card lies on top of the others, so the thing under the finger is almost
    /// never the thing the finger meant.
    /// </para>
    /// </summary>
    private bool Take(ItemId item, System.Windows.Point origin)
    {
        if (_context is not { } context
            || _scene.Items.FirstOrDefault(one => one.ItemId == item) is not { } current)
        {
            return false;
        }

        var (width, height) = Surface(context);
        var at = new CorePoint(width <= 0 ? 0 : origin.X / width, height <= 0 ? 0 : origin.Y / height);

        // The fan first, and the suppression is asked of the card the POINT picks rather than of
        // the one that answered the hit test - those are different cards nearly every time.
        if (current.Parked)
        {
            return Grip(at, context);
        }

        if (!CoreManipulation.AcceptsGestures(_scene, current, State))
        {
            // The one answer for all three reasons - padlock, disabled screen, focus lying. A
            // player who gets nothing at all presses harder and decides the table is broken.
            Refuse(item);

            return false;
        }

        _held[item] = new Hold(current)
        {
            TapDip = origin,
            Tap = at,
        };

        // Grabbed: what is taken hold of comes to the front, locally at once and bindingly from the
        // hub right afterwards (Part 3).
        Report(item, grabbed: true, binding: false);

        return true;
    }

    /// <summary>
    /// A hand has landed on the fan. Picks the card the point means and shows it; from here the
    /// gesture is either a run along the fan or a pull away from it.
    /// </summary>
    private bool Grip(CorePoint at, ScreenContext context)
    {
        if (Parking.Pick(_scene, context, at) is not { } card)
        {
            return false;
        }

        if (_scene.Items.FirstOrDefault(one => one.ItemId == card) is not { } picked
            || !CoreManipulation.AcceptsGestures(_scene, picked, State))
        {
            Refuse(card);

            return false;
        }

        _fan = new Fanning(card);
        Peek(card, context);

        return true;
    }

    /// <summary>
    /// One step of a hand that is on the fan: run along it and the shown card changes, pull away
    /// from it and that card comes out onto the table.
    /// </summary>
    /// <returns>Whether the gesture is still the fan's - <c>false</c> once the card is out.</returns>
    private bool Fanned(CorePoint now, System.Windows.Point origin, ScreenContext context)
    {
        if (_fan is not { Taken: false } fanning)
        {
            return false;
        }

        if (Parking.OnTheFan(now, context))
        {
            if (Parking.Pick(_scene, context, now) is { } next && next != fanning.Card)
            {
                Unpeek(fanning.Card, context);
                fanning.Card = next;

                // At the card's OWN place in the fan - the next one steps out further along,
                // where it lies, rather than at the spot the finger first touched (hand-run of M3).
                Peek(fanning.Card, context);
            }

            return true;
        }

        // The hand has left the band, so the card comes out. <b>That boundary and no other</b>: it
        // is the same line that decides parking, so the fan owns exactly the band and the table
        // owns the rest. Measured from the band rather than from where the hand landed, which was
        // the fault - the old test wanted the pull to be longer than the run along the fan, so
        // after a good scroll it took half a screen to get a card out (hand-run of M3).
        //
        // The picture keeps the place the peek gave it, so nothing jumps and the hand carries on.
        if (_scene.Items.FirstOrDefault(one => one.ItemId == fanning.Card) is not { } card
            || Parking.Peek(_scene, context, fanning.Card) is not { } peek)
        {
            return false;
        }

        fanning.Taken = true;

        _held[fanning.Card] = new Hold(card with { CenterX = peek.X, CenterY = peek.Y, Parked = false })
        {
            TapDip = origin,
            Tap = now,
        };

        Parked?.Invoke(fanning.Card, false);
        Report(fanning.Card, grabbed: true, binding: false);

        return false;
    }

    /// <summary>Draws one card clear of the fan, whole, at its own place along it.</summary>
    private void Peek(ItemId card, ScreenContext context)
    {
        if (_scene.Items.FirstOrDefault(one => one.ItemId == card) is not { } item
            || !_mounts.TryGetValue(card, out var mount)
            || Parking.Peek(_scene, context, card) is not { } at)
        {
            return;
        }

        Place(mount, item with { CenterX = at.X, CenterY = at.Y }, context);
        Trim(mount, Parking.Cut.Whole, context);
        Panel.SetZIndex(mount.Element, HeldAbove);
    }

    /// <summary>And puts it back, when the hand has moved on to the next one or let go.</summary>
    private void Unpeek(ItemId card, ScreenContext context)
    {
        if (_scene.Items.FirstOrDefault(one => one.ItemId == card) is not { } item
            || !_mounts.TryGetValue(card, out var mount))
        {
            return;
        }

        Place(mount, item, context);
        Trim(mount, Parking.CutOf(item, context), context);
        Panel.SetZIndex(mount.Element, Parking.Depth(_scene, item));
    }

    /// <summary>The end of a fan gesture that never took a card out: nothing happened.</summary>
    private void Release(ScreenContext context)
    {
        if (_fan is not { Taken: false } fanning)
        {
            _fan = null;

            return;
        }

        Unpeek(fanning.Card, context);
        _fan = null;
    }

    /// <summary>
    /// The item and place a gesture is really about. Once a card has come out of the fan, the WPF
    /// events keep arriving on the element that answered the first hit test - which is some other
    /// card of the fan.
    /// </summary>
    private (ItemId Item, Mount Mount) Aimed(ItemId item, Mount mount) =>
        _fan is { Taken: true } fan && _mounts.TryGetValue(fan.Card, out var theirs)
            ? (fan.Card, theirs)
            : (item, mount);

    /// <summary>One step of a hand on a picture, inertial or not.</summary>
    private void Move(ItemId item, Mount mount, ManipulationDeltaEventArgs e)
    {
        e.Handled = true;

        // Inertia is the system's own arithmetic and arrives on our own clock, so it would report a
        // latency nobody felt. Only a real hand counts.
        if (!e.IsInertial)
        {
            HandWaited?.Invoke(Environment.TickCount - e.Timestamp);
        }

        if (_context is not { } context)
        {
            e.Complete();

            return;
        }

        var (width, height) = Surface(context);

        if (_fan is { Taken: false })
        {
            var origin = e.ManipulationOrigin;

            Fanned(
                new CorePoint(width <= 0 ? 0 : origin.X / width, height <= 0 ? 0 : origin.Y / height),
                origin,
                context);

            return;
        }

        (item, mount) = Aimed(item, mount);

        if (!_held.TryGetValue(item, out var hold))
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

        // A glide stops before the fan; a hand may push right up to it. A picture that slid to a
        // stop under the fan would be parked without being parked - the fan is drawn over the
        // table, so nothing of it could be picked up again.
        var (moved, turning) = CoreManipulation.Step(
            hold.Item, hold.Turning, step, context, gliding: e.IsInertial);

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
    /// Writes down what a release towards the park edge really measured - speed, distance and the
    /// pressure against the clamp - whether it parked or not.
    /// <para>
    /// Both ways into the fan rest on numbers that were proposed rather than measured, and the
    /// first hand-run could say they were wrong without being able to say by how much. This is
    /// what turns the next run into arithmetic (Guide G6).
    /// </para>
    /// </summary>
    private void Measured(Hold hold, CorePoint release)
    {
        if (_context is not { } context)
        {
            return;
        }

        var edge = CoreManipulation.FromParkEdge(release, context);

        if (hold.Towards <= 0 && edge > 4 * CoreManipulation.ParkFlickDip)
        {
            return;
        }

        var outcome = hold.Parked
            ? "parked by the flick"
            : Parking.OnTheFan(release, context)
                ? "parked by the hand on the fan"
                : "moved";

        ParkMeasured?.Invoke(
            (long)Math.Round(hold.Towards),
            (long)Math.Round(hold.Moved),
            (long)Math.Round(edge),
            outcome);
    }

    /// <summary>
    /// Puts a picture into the slot bar at the end of a gesture and falls silent.
    /// <para>
    /// The silence is the point: where a parked picture lies follows from the LIST of parked
    /// pictures, so a transform reported afterwards would answer a question this gesture does not
    /// get to answer - and it would win, because it arrives second. And the redraw has to be asked
    /// for, because a held picture is deliberately passed over by the drawing (conflict rule 3) and
    /// this way out reports nothing that would trigger one.
    /// </para>
    /// </summary>
    private void Park(ItemId item)
    {
        if (!_held.TryGetValue(item, out var hold))
        {
            return;
        }

        hold.Parked = true;
        _held.Remove(item);

        Parked?.Invoke(item, true);
        Settled?.Invoke();
    }

    /// <summary>
    /// The fingers have left and the picture would now glide. <b>This is where the park decision is
    /// read</b>, because it is the moment the swipe had its speed - after the glide that speed is
    /// nearly zero (Part 6).
    /// </summary>
    private void Fling(ItemId item, ManipulationInertiaStartingEventArgs e)
    {
        e.Handled = true;

        if (_fan is { Taken: false })
        {
            // A hand that only ran along the fan glides nowhere.
            Still(e);

            return;
        }

        item = _fan is { Taken: true } taken ? taken.Card : item;

        if (_context is not { } context || !_held.TryGetValue(item, out var hold))
        {
            return;
        }

        // WPF measures in DIP per millisecond, the rule is written in DIP per second.
        var velocity = e.InitialVelocities.LinearVelocity;

        hold.Towards = CoreManipulation.Towards(velocity.X * 1000, velocity.Y * 1000, context);

        // <b>Both roads into the fan are read HERE, at the moment the hand left</b> - one by speed,
        // one by place. The place used to be read only after the glide had run itself out, so a
        // picture let go on the fan drifted a little further and dropped in afterwards: "it should
        // land in the fan at once and not run its momentum out there first" (hand-run of M3, N1).
        // Speed has to be read here anyway, because after the glide there is none left (Part 6),
        // and reading the two at different moments was what made the one look slower than the other.
        var onTheFan = Parking.OnTheFan(Normalised(e.ManipulationOrigin, context), context);

        if (onTheFan
            || CoreManipulation.ShouldPark(hold.Item, velocity.X * 1000, velocity.Y * 1000, hold.Moved, context))
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

        if (_context is not { } context)
        {
            return;
        }

        if (_fan is { Taken: false })
        {
            // Let go on the fan without pulling anything out: nothing happens to it (Part 6).
            Release(context);

            return;
        }

        (item, mount) = Aimed(item, mount);
        _fan = null;

        if (!_held.TryGetValue(item, out var hold))
        {
            return;
        }

        Measured(hold, Normalised(e.ManipulationOrigin, context));

        if (hold.Parked)
        {
            // Where a parked picture lies is the bar's business, and the bar is worked out at both
            // ends from the LIST. A binding transform here would be this gesture answering a
            // question it does not get to answer - and it would win, because it arrives second.
            _held.Remove(item);

            // And drawn again, which is the half that was missing (hand-run of M3, step 18): the
            // hub's patch for the park usually arrives while the hand is still counted as holding,
            // and a held picture is deliberately passed over by the drawing (conflict rule 3). So
            // the patch was applied to the scene and never reached the glass, the picture stayed
            // lying where it was, and it jumped into the bar only when somebody next touched it.
            // The other way out of this method reports a binding transform and is drawn by the
            // application; this one reports nothing, so it has to ask.
            Settled?.Invoke();

            return;
        }

        if (Parking.OnTheFan(Normalised(e.ManipulationOrigin, context), context))
        {
            // Let go with the hand on the fan - the slow way to tidy up, and the only one a mouse
            // has (Part 6, end of M3). Read HERE and not in Fling, because a push has no velocity
            // and WPF has no inertia to announce.
            Park(item);

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

        // Asked to the front a second time when something overtook this picture while it was being
        // pushed. Without it the hand wins only until it lets go: the picture is drawn on top for
        // the length of the gesture and then drops back under the arrival, which reads as the
        // table taking the picture away at the exact moment it was put down.
        Report(item, grabbed: Overtaken(item), binding: true);

        _held.Remove(item);
    }

    /// <summary>
    /// Whether anything lies over this picture right now - an arrival, or one somebody else brought
    /// to the front while this hand was busy.
    /// <para>
    /// Read from the scene at the moment of release rather than remembered from the grab, and the
    /// difference matters: what the grab was granted is the hub's answer, which the display learns
    /// only when the patch comes back. Asking "is it still on top?" needs no memory and is right
    /// whenever it is asked.
    /// </para>
    /// </summary>
    private bool Overtaken(ItemId item)
    {
        if (_scene.Items.FirstOrDefault(candidate => candidate.ItemId == item) is not { } mine)
        {
            return false;
        }

        return _scene.Items.Any(candidate => candidate.ItemId != item && candidate.ZOrder > mine.ZOrder);
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
        if (e.LeftButton != MouseButtonState.Pressed || _context is not { } context)
        {
            return;
        }

        var now = e.GetPosition(_stage);
        var (width, height) = Surface(context);

        if (_fan is { Taken: false })
        {
            // The same two halves as under a finger: running along the fan chooses, pulling away
            // takes. A mouse has no other way into the fan, and it needs none.
            Fanned(
                new CorePoint(width <= 0 ? 0 : now.X / width, height <= 0 ? 0 : now.Y / height),
                now,
                context);

            _mouseAt = now;
            e.Handled = true;

            return;
        }

        (item, mount) = Aimed(item, mount);

        if (!_held.TryGetValue(item, out var hold))
        {
            return;
        }

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

        if (_context is not { } context)
        {
            return;
        }

        if (_fan is { Taken: false })
        {
            Release(context);
            e.Handled = true;

            return;
        }

        (item, mount) = Aimed(item, mount);
        _fan = null;

        if (!_held.TryGetValue(item, out var hold))
        {
            return;
        }

        if (Parking.OnTheFan(Normalised(e.GetPosition(_stage), context), context))
        {
            // The one route into the fan a mouse has at all: it cannot flick, so without this a
            // display PC without touch could never park anything (Part 6, end of M3).
            Park(item);
            e.Handled = true;

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

        if (_scene.Items.FirstOrDefault(one => one.ItemId == item) is not { } current || current.Parked)
        {
            // The fan has no zoom: a card is at the size its picture arrives at, and a wheel over
            // the fan would make one card unlike its neighbours for no reason anybody asked for.
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
    /// <summary>A point on the stage in the normalised coordinates the scene is written in.</summary>
    private CorePoint Normalised(System.Windows.Point at, ScreenContext context)
    {
        var (width, height) = Surface(context);

        return new CorePoint(width <= 0 ? 0 : at.X / width, height <= 0 ? 0 : at.Y / height);
    }

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
    /// <summary>
    /// Hangs the progress ring on a picture, moves it along, or takes it off again. A
    /// <paramref name="fraction"/> below zero means "nothing is coming for this one".
    /// <para>
    /// <b>It rides ON the picture, like the caption, and that took a hand-run to arrive at.</b> The
    /// rings used to live on a layer above the whole scene, which had two consequences the table
    /// showed at once: a ring was drawn over pictures LYING ON TOP of the one it belonged to - so
    /// whoever looked ascribed it to the wrong picture - and it was set down afresh at a computed
    /// place on every drawing instead of being carried by the picture, so it lagged behind under a
    /// finger. Inside the place both go away for nothing: the depth is the item's own, and the
    /// movement is the item's own.
    /// </para>
    /// <para>
    /// <b>What was given up for it, said plainly:</b> a ring on a covered picture is now covered
    /// too. That was the argument for the layer above, and it does not hold - a ring over a
    /// stranger's picture is not weaker information, it is wrong information, and if the covered
    /// picture finishes before anybody brings it to the front, the ring was the only thing ever
    /// seen of it and it stood in the wrong place. The question the layer really answered - "is
    /// this table still busy?" - belongs to the control's list, which carries the whole run since
    /// <see cref="AssetLoadState.Waiting"/> told the two apart.
    /// </para>
    /// </summary>
    private static void Turning(Mount mount, double fraction)
    {
        if (fraction < 0)
        {
            if (mount.Ring is { } gone)
            {
                mount.Element.Children.Remove(gone);
                mount.Ring = null;
            }

            return;
        }

        // Measured against the picture it belongs to, because 56 DIP is wider than the smallest
        // picture the table allows: MinScale is 80 DIP of height by default, and a portrait at that
        // height is about 53 DIP across. Below the floor there is no ring at all - a circle of
        // twelve pixels is a smudge, not a reading.
        var edge = Math.Min(mount.Element.Width, mount.Element.Height);
        var size = Math.Clamp(edge * 0.6, RingFloor, RingSize);

        if (double.IsNaN(size) || size < RingFloor)
        {
            if (mount.Ring is { } small)
            {
                mount.Element.Children.Remove(small);
                mount.Ring = null;
            }

            return;
        }

        var stroke = size / 11;

        if (mount.Ring is null)
        {
            var ring = new Grid
            {
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            };

            ring.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
            });

            // An arc rather than a second ellipse: a partial circle is what says "some of it", and
            // a ring that only changed colour would read as a state rather than as a quantity.
            ring.Children.Add(new System.Windows.Shapes.Path
            {
                Stroke = Brushes.White,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });

            mount.Element.Children.Add(ring);
            mount.Ring = ring;
        }

        mount.Ring.Width = size;
        mount.Ring.Height = size;

        // Against the picture's own turn, so twelve o'clock stays up. A tilted picture may be
        // tilted; how far it has got may not.
        mount.Ring.RenderTransform = new RotateTransform(-mount.Turn.Angle);

        ((System.Windows.Shapes.Ellipse)mount.Ring.Children[0]).StrokeThickness = stroke;

        var filled = (System.Windows.Shapes.Path)mount.Ring.Children[1];

        filled.StrokeThickness = stroke;
        filled.Data = Arc(size / 2, (size / 2) - (stroke * 0.8), Math.Clamp(fraction, 0, 1));
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

        // It answers the hit test but gets no gesture handlers, and both halves are the point
        // (Part 6): the background takes no gestures, AND touches on it are swallowed rather than
        // handed down - whatever runs underneath is completely covered by it, so a finger there
        // would be poking blindly into a map nobody can see. Raise() alone leaves the place at
        // IsHitTestVisible = false, which is what an item needs until Handle() turns it on; a
        // background never goes through Handle(), so it says so here.
        _backdrop.Element.IsHitTestVisible = true;

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
    /// Cuts a card down to what the fan shows of it, and <b>fades the cut so it reads as "there is
    /// more of this"</b> rather than as the edge of the picture.
    /// <para>
    /// It is the arrival fade turned from time into space, and deliberately the same idea: a
    /// picture coming in fades from nothing to itself over a moment, a cut card fades from itself
    /// to nothing over a finger's width. Same principle, one lasting instead of passing.
    /// </para>
    /// <para>
    /// <b>A mask and no clip.</b> The mask cuts and fades in one stroke - everything before the
    /// first stop is transparent - where a clip would need a second, separate statement of the same
    /// edge and would still leave a hard line for the fade to argue with. It also leaves hit
    /// testing alone, which is right: what a hand on the fan means is worked out from its POSITION
    /// (<see cref="Parking.Pick"/>), never from the element it happened to land on.
    /// </para>
    /// </summary>
    private static void Trim(Mount mount, Parking.Cut cut, ScreenContext context)
    {
        if (cut.IsWhole)
        {
            mount.Element.OpacityMask = null;

            return;
        }

        // The fan runs down a side bar and across a top or bottom one, and the cut takes the HEAD -
        // the end pointing back towards the near end of the fan, which is where the card in front
        // of it lies anyway.
        var alongY = context.ParkEdge is ParkEdge.Left or ParkEdge.Right;

        var start = 1 - cut.Shown;
        var full = Math.Min(1, start + cut.Fade);

        mount.Element.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = alongY ? new System.Windows.Point(0, 1) : new System.Windows.Point(1, 0),
            GradientStops =
            [
                new GradientStop(Colors.Transparent, start),
                new GradientStop(Colors.Black, full),
            ],
        };
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

        // The same argument the animation budget makes (Part 6): a hundred highlights at once are
        // a hundred animations on the UI thread, and they say nothing - when everything is new,
        // nothing stands out. The point of the highlight is a thirteenth picture among twelve.
        if (_flashes >= AnimationBudget.DefaultMaximum)
        {
            return;
        }

        _flashes++;

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
        fade.Completed += (_, _) =>
        {
            mount.Element.Children.Remove(pane);
            _flashes--;
        };

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
    /// <summary>
    /// A hand on the fan: which card it is showing. Local to the display and never sent - until a
    /// card comes out, nothing has happened to the scene (Part 6).
    /// </summary>
    private sealed class Fanning(ItemId card)
    {
        /// <summary>The card being shown, which changes as the hand runs along the fan.</summary>
        internal ItemId Card { get; set; } = card;

        /// <summary>Whether the card has come out; from then on this is an ordinary push.</summary>
        internal bool Taken { get; set; }
    }

    private sealed class Hold(SceneItem item)
    {
        /// <summary>The live local values.</summary>
        internal SceneItem Item { get; set; } = item;

        internal Turning Turning { get; set; } = Turning.Beginning;

        /// <summary>How far the hand has travelled in DIP - a tap is a gesture that barely moved.</summary>
        internal double Moved { get; set; }

        /// <summary>
        /// How fast the release was heading for the park edge, in DIP per second. Read once when
        /// inertia is announced, kept only so the measurement line can report it.
        /// </summary>
        internal double Towards { get; set; }

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

        /// <summary>The progress ring, while this picture is still coming.</summary>
        internal Grid? Ring { get; set; }

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
