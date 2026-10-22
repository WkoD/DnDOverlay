namespace DnDOverlay.Display;

/// <summary>
/// Whether a pairing request puts the setup screen up.
/// <para>
/// <b>Not when the request is an answer to a call for orphaned displays.</b> The answer is the
/// ordinary <c>Hello</c> (Part 4), and the caller does not know its token, so it lands as a pairing
/// request like any other. But what shows it at the device is the rescue mark, whose second line is
/// the veto (Part 4, Part 6: "the mark is the veto, not the gate"). An opaque, topmost setup screen on
/// every monitor lies over that mark and swallows every touch - at the one moment the veto exists -
/// and over monitors the DM gave back to Windows. The setup screen belongs to a device whose pairing
/// the DM compares at the table; an orphan is taken over from the control, without walking.
/// </para>
/// </summary>
internal static class SetupScreen
{
    internal static bool Wanted(bool answeringCall) => !answeringCall;
}
