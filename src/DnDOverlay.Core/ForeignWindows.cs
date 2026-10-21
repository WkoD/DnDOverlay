namespace DnDOverlay.Core;

/// <summary>
/// A window of another program on a display PC (Part 4, Part 6, Part 7) - MapTool beneath, a rulebook,
/// a browser, the Windows update dialog. <b>The program, never the title</b> (idea 9): titles carry page
/// names, subject lines, file names; for "what lies there and where?" the program goes just as far.
/// </summary>
/// <param name="Handle">Opaque: valid only on its machine and for the window's life; checked against <paramref name="Program"/> before every command.</param>
/// <param name="Bounds">Normalised to its screen, 0 to 1 on both axes - the visible frame, without the invisible shadow border.</param>
/// <param name="ZOrder">0 is topmost; the larger, the further down.</param>
public sealed record ForeignWindow(long Handle, ScreenId ScreenId, string Program, Rect Bounds, int ZOrder, bool Minimized);

/// <summary>What the control asks of a foreign window.</summary>
public enum WindowAction
{
    /// <summary>Puts the window at a place and size - restored first, if it was maximised.</summary>
    Move,
    Minimize,
    Maximize,
    Restore,
}

/// <summary>What became of a command on a foreign window - read back after the act, not assumed (Part 6).</summary>
public enum WindowOutcome
{
    Done,

    /// <summary>The program runs elevated; Windows does not let an ordinary process touch it.</summary>
    Elevated,

    /// <summary>The window is gone, or the handle now belongs to another program.</summary>
    Gone,

    /// <summary>The screen is inactive - the DM handed it back, somebody works there.</summary>
    Inactive,

    /// <summary>The device reports its windows but does not let them be laid.</summary>
    NotAllowed,
}
