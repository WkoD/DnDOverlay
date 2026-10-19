using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Core;

/// <summary>What a finger's stroke on the campaign panel means, once the first millimetres are behind it.</summary>
public enum PanelStroke
{
    /// <summary>Not far enough yet to tell - nothing happens until it is.</summary>
    Undecided,

    /// <summary>Along the docking edge: the list scrolls.</summary>
    Scroll,

    /// <summary>Across it, toward the stage: the entry comes out of the list.</summary>
    Out,

    /// <summary>Across it, toward the window's edge: a retracted panel that was opened goes back.</summary>
    In,
}

/// <summary>
/// Scrolling and pulling out told apart by direction, not by where the finger started (Part 7).
/// <para>
/// <b>The direction is not a free choice; it falls out of the arrangement.</b> The panel always
/// scrolls along its long axis, and the stage always lies across it - so "toward the stage" is
/// necessarily "out of the list". The opposite way across, toward the edge the panel docks at, is
/// the swipe that closes a retracted panel (Part 7: "a swipe toward the edge").
/// </para>
/// <para>
/// <b>Decided once and kept</b> for the length of the stroke - that is the caller's half: it asks
/// until the answer is no longer <see cref="PanelStroke.Undecided"/> and then stops asking, or a
/// movement would flip over in the middle of a drag. With a mouse the question does not arise: the
/// wheel scrolls.
/// </para>
/// </summary>
public static class PanelGesture
{
    /// <summary>
    /// How far a finger travels before its direction counts, in DIP - about two and a half millimetres
    /// at 96 DPI. Larger than the tolerance that decides whether a hand moved at all, because an angle
    /// read off the first three DIP is mostly the finger rolling.
    /// </summary>
    public const double Decision = 10;

    /// <summary>
    /// The stroke from where the finger went down to where it is now, for a panel docked at
    /// <paramref name="side"/>. A diagonal exactly between the two counts as scrolling: the list is
    /// what the hand is over, and a picture pulled out by accident lands on a screen.
    /// </summary>
    public static PanelStroke Decide(double dx, double dy, PanelSide side)
    {
        if ((dx * dx) + (dy * dy) < Decision * Decision)
        {
            return PanelStroke.Undecided;
        }

        var lying = side is PanelSide.Top or PanelSide.Bottom;
        var along = Math.Abs(lying ? dx : dy);
        var across = lying ? dy : dx;

        if (along >= Math.Abs(across))
        {
            return PanelStroke.Scroll;
        }

        // Away from the docking edge is toward the stage.
        var towardStage = side switch
        {
            PanelSide.Left or PanelSide.Top => across > 0,
            _ => across < 0,
        };

        return towardStage ? PanelStroke.Out : PanelStroke.In;
    }
}
