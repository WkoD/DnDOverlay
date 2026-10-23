using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control;

/// <summary>
/// Where the campaign panel and the log panel dock, and how wide they may be (Part 7).
/// <para>
/// <b>The stage must not disappear</b>: the campaign panel is at least a row of tiles wide and at most
/// half the window; the log panel leaves the stage <see cref="StageMinimum"/> beside whatever the
/// campaign panel takes on the same axis, and gives way - it opens narrower - rather than press the
/// stage under it. <b>The log never docks at the campaign panel's edge</b>: it goes to the opposite one
/// when that edge is taken.
/// </para>
/// </summary>
internal static class Docking
{
    /// <summary>The narrowest either panel is laid, in DIP.</summary>
    internal const double Narrowest = 160;

    /// <summary>The room the stage keeps between the two panels, in DIP - a row of tiles still fits.</summary>
    internal const double StageMinimum = 320;

    /// <summary>The widest the campaign panel may be: half the window along its axis, and never under 200.</summary>
    internal static double PanelLimit(double extent) => Math.Max(200, extent / 2);

    /// <summary>The widest the log panel may be: the room left once the campaign panel and the stage have theirs.</summary>
    /// <param name="taken">What the campaign panel takes on the same axis - nothing when it is retracted or across the other one.</param>
    internal static double LogLimit(double room, double taken) => Math.Max(Narrowest, room - taken - StageMinimum);

    /// <summary>A kept size held between <see cref="Narrowest"/> and the limit.</summary>
    internal static double Length(double size, double limit) => Math.Clamp(size, Narrowest, limit);

    /// <summary>The log's edge: the kept one, the panel's opposite when none is kept - and never the panel's own.</summary>
    internal static PanelSide LogSide(PanelSide? kept, PanelSide panel)
    {
        var side = kept ?? Opposite(panel);

        return side == panel ? Opposite(panel) : side;
    }

    /// <summary>The edge across from this one.</summary>
    internal static PanelSide Opposite(PanelSide side) =>
        side switch
        {
            PanelSide.Left => PanelSide.Right,
            PanelSide.Right => PanelSide.Left,
            PanelSide.Top => PanelSide.Bottom,
            _ => PanelSide.Top,
        };
}
