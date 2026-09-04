using System.Windows;
using System.Windows.Media;

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
/// <b>What is NOT collected here</b> is the loading fill and the head (Part 7, rank 3 before 4):
/// they are the answer to "is something happening at all", so they must keep going when the
/// drawing of the scene is behind. They are separate elements and invalidate themselves.
/// </para>
/// </summary>
internal static class Redraw
{
    private static readonly HashSet<UIElement> Waiting = [];

    private static bool _hooked;

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
        foreach (var element in Waiting)
        {
            element.InvalidateVisual();
        }

        Waiting.Clear();

        CompositionTarget.Rendering -= Tick;
        _hooked = false;
    }
}
