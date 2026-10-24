namespace DnDOverlay.Display;

/// <summary>
/// Whether a pairing request puts the setup screen up.
/// <para>
/// <b>Not when the request is an answer to a call for orphaned displays.</b> The answer is the
/// ordinary <c>Hello</c> (Part 4), and the caller does not know its token, so it lands as a pairing
/// request like any other. But what shows it at the device is the rescue mark, whose second line is
/// the veto (Part 4, Part 6: "the mark is the veto, not the gate"). Decided while the setup screen was
/// still opaque and full-screen and lay over that mark; the rule stands with the small card too
/// (24.10.2026), because the card asks the DM to allow the device - a second notice beside the mark,
/// saying the opposite of its veto. The card belongs to a device whose pairing the DM compares; an
/// orphan is taken over from the control, without walking.
/// </para>
/// </summary>
internal static class SetupScreen
{
    internal static bool Wanted(bool answeringCall) => !answeringCall;
}
