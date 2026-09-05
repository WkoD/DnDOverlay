using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using CorePoint = DnDOverlay.Core.Point;
using TilePoint = System.Windows.Point;

namespace DnDOverlay.Control;

/// <summary>
/// Where fingers are lying on the table, drawn in the thumbnail: a circle at the head of every
/// touch and a narrow trail behind it that fades away (Part 7).
/// <para>
/// <b>Both halves are needed, and each answers a different failure.</b> Without the after-glow the
/// DM would see nothing at all of a short tap - the commonest thing that happens, when a player
/// points at something. Without the TRAIL he would see nothing intelligible of the second
/// commonest: a player tracing the way the group wants to take, which out of ten samples a second
/// would be a string of beads with no direction in it.
/// </para>
/// <para>
/// <b>One line per touch, and that is what the identifier is for</b> (Part 4). Two people pointing
/// give two lines rather than one zigzag between them - which is exactly what a drawing keyed on
/// nothing but position would produce.
/// </para>
/// <para>
/// <b>No contradiction with the 300 ms decay in Part 4:</b> there the DATA expire, here a DRAWING
/// fades. The points carry their own age, so a trail assembled out of two merged messages is even
/// at the seam.
/// </para>
/// <para>
/// <b>Its own layer and its own clock.</b> The scene is drawn once per render pass and may lag; this
/// has to keep fading whether anything else moves or not, and it stops its clock the moment the
/// last trail has gone - a timer running through an evening in which nobody touches the table is
/// the opposite of what this is for.
/// </para>
/// </summary>
internal sealed class Fingers : FrameworkElement
{
    private readonly Dictionary<long, Trail> _trails = [];
    private readonly DispatcherTimer _fading = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private ScreenContext _screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
    private ViewRotation _view;

    /// <summary>
    /// How long a drawing takes to die away, in milliseconds (Part 7: about a second and a half).
    /// </summary>
    private const double FadeMs = 1500;

    /// <summary>The circle at the head of a touch, in DIP. It marks a place, so it does not scale.</summary>
    private const double Head = 18;

    internal Fingers()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        _fading.Tick += (_, _) => Fade();
    }

    /// <summary>How the DM is looking at this table. The fingers turn with it, like everything else.</summary>
    internal void Show(ScreenContext screen, ViewRotation view)
    {
        ArgumentNullException.ThrowIfNull(screen);

        _screen = screen;
        _view = view;
    }

    /// <summary>
    /// What is lying on this screen right now.
    /// <para>
    /// <b>An empty list is a statement rather than silence</b> (Part 4): it goes out once when the
    /// last finger lifts, and it takes the circles away at once. The trails behind them still fade
    /// out - the finger is gone, the movement it made is what the DM is still reading.
    /// </para>
    /// </summary>
    internal void Report(IReadOnlyList<TouchTrail> touches)
    {
        ArgumentNullException.ThrowIfNull(touches);

        var now = Environment.TickCount64;

        foreach (var trail in _trails.Values)
        {
            trail.Down = false;
        }

        foreach (var touch in touches)
        {
            if (!_trails.TryGetValue(touch.Touch, out var trail))
            {
                trail = new Trail();
                _trails[touch.Touch] = trail;
            }

            trail.Down = true;

            foreach (var point in touch.Points)
            {
                // Stamped with the moment it actually happened, not the moment it arrived: the age
                // travels with the point precisely so a merged trail is not uneven at the join.
                trail.Points.Add(new Mark(point.X, point.Y, now - point.AgeMs));
            }

            while (trail.Points.Count > TouchTrail.MaxPoints)
            {
                trail.Points.RemoveAt(0);
            }
        }

        if (_trails.Count > 0 && !_fading.IsEnabled)
        {
            _fading.Start();
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        base.OnRender(drawingContext);

        if (_trails.Count == 0 || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        var now = Environment.TickCount64;

        foreach (var trail in _trails.Values)
        {
            Draw(drawingContext, trail, now);
        }
    }

    /// <summary>
    /// One touch: the trail behind, then the circle at its head. Drawn as segments rather than as
    /// one polyline, because each segment has its own age and therefore its own opacity - a line
    /// that faded as a whole would say the finger was everywhere at once.
    /// </summary>
    private void Draw(DrawingContext drawingContext, Trail trail, long now)
    {
        for (var i = 1; i < trail.Points.Count; i++)
        {
            var from = trail.Points[i - 1];
            var to = trail.Points[i];
            var left = Left(to, now);

            if (left <= 0)
            {
                continue;
            }

            drawingContext.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb((byte)(left * 200), 0xFF, 0xD7, 0x00)), 3),
                Where(from),
                Where(to));
        }

        if (!trail.Down || trail.Points.Count == 0)
        {
            return;
        }

        var head = trail.Points[^1];

        drawingContext.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD7, 0x00)),
            new Pen(Brushes.Gold, 2),
            Where(head),
            Head / 2,
            Head / 2);
    }

    /// <summary>How much of a point is left, from 1 at the moment it happened to 0 when it is gone.</summary>
    private static double Left(Mark mark, long now) => Math.Clamp(1 - ((now - mark.When) / FadeMs), 0, 1);

    private TilePoint Where(Mark mark) =>
        Placing.InTile(new CorePoint(mark.X, mark.Y), _view, RenderSize);

    /// <summary>
    /// Drops what has died away and stops the clock when nothing is left. <b>A trail that gets no
    /// more points dies of its own accord</b>, which is the answer to a network cut with fingers
    /// still on the table: they go out instead of standing there as ghosts (Prüfschritt 37c).
    /// </summary>
    private void Fade()
    {
        var now = Environment.TickCount64;

        foreach (var (touch, trail) in _trails.ToList())
        {
            trail.Points.RemoveAll(mark => Left(mark, now) <= 0);

            if (trail.Points.Count == 0)
            {
                _ = _trails.Remove(touch);
            }
        }

        if (_trails.Count == 0)
        {
            _fading.Stop();
        }

        InvalidateVisual();
    }

    private sealed record Mark(double X, double Y, long When);

    private sealed class Trail
    {
        internal List<Mark> Points { get; } = [];

        /// <summary>Whether the finger is still on the table - only then is a head drawn.</summary>
        internal bool Down { get; set; }
    }
}
