using System.Windows;
using System.Windows.Media;
using DnDOverlay.Rendering.Windows;

namespace DnDOverlay.Control;

/// <summary>
/// Collects what has to be drawn again and draws it <b>once per render pass</b>.
/// <para>
/// Three pictures moved at the table are about sixty patches a second, and each one may touch
/// several tiles. Handed through one at a time, the stage would redraw itself dozens of times
/// between two frames - work that is thrown away by definition, and it would arrive exactly when
/// something is happening at the table (Part 7).
/// </para>
/// <para>
/// <b>The hook is taken off again</b> when nothing is waiting. <c>CompositionTarget.Rendering</c>
/// fires on every frame for as long as anybody listens, so a permanent subscription would keep a
/// window busy through an evening in which nothing moves - the opposite of what this is for.
/// </para>
/// <para>
/// <b>It comes off one pass later than it used to</b>, and that pays for the frame counter. The
/// hook used to be dropped in the same pass that drew, so a drag hooked and unhooked sixty times a
/// second - each frame its own little subscription, and no two consecutive ticks belonging to the
/// same stretch. There was nothing to measure an interval against. Now it is dropped on the first
/// pass that finds nothing waiting, which costs <b>one idle frame at the end of a burst</b> and
/// leaves a continuous tick stream for as long as the stage is actually drawing - which is the only
/// stretch its frame time says anything about (see <see cref="FrameWatch.WhileDrawing"/>).
/// </para>
/// <para>
/// <b>What is NOT collected here</b> is the loading fill and the head (Part 7, rank 3 before 4):
/// they are the answer to "is something happening at all", so they must keep going when the
/// drawing of the scene is behind. They are separate elements and invalidate themselves.
/// </para>
/// </summary>
internal static class Redraw
{
    private static readonly HashSet<UIElement> Waiting = [];

    private static bool _hooked;

    private static FrameWatch? _watch;

    /// <summary>
    /// The counter that is to be told about the passes this drives, or <see langword="null"/> for
    /// none. Set once at start-up; the watch belongs to the window and is disposed with it.
    /// </summary>
    internal static void Measure(FrameWatch? watch) => _watch = watch;

    /// <summary>Asks for this element to be drawn again at the next render pass.</summary>
    internal static void Ask(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        Waiting.Add(element);

        if (_hooked)
        {
            return;
        }

        CompositionTarget.Rendering += Tick;
        _hooked = true;
    }

    private static void Tick(object? sender, EventArgs e)
    {
        if (Waiting.Count == 0)
        {
            // The burst is over: nobody asked for anything between the last pass and this one. The
            // hook goes, and the counter is told - the gap that starts here is not a frame, it is
            // an evening in which nothing moved.
            CompositionTarget.Rendering -= Tick;
            _hooked = false;
            _watch?.Rested();

            return;
        }

        foreach (var element in Waiting)
        {
            element.InvalidateVisual();
        }

        Waiting.Clear();

        if (e is RenderingEventArgs rendering)
        {
            _watch?.Ticked(rendering.RenderingTime);
        }
    }
}
