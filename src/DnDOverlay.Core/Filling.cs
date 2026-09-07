namespace DnDOverlay.Core;

/// <summary>
/// How much of one run of arrivals goes onto the screen.
/// <para>
/// <b>Measured at the table, 07.09.2026:</b> one intake over 723 files put <b>714</b> pictures onto
/// a single screen, one <c>AddItem</c> each. The hub did not mind - 714 additions with the control's
/// four scene reads apiece cost <b>30 ms</b> - and the display drew them at its full rate. What
/// broke was everything either side of that: the display held <b>2.9 GB</b> of decoded pictures,
/// and the control's tile stopped answering altogether, so nothing on that screen could be picked
/// any more.
/// </para>
/// <para>
/// <b>And 708 of them were never visible in the first place.</b> <see cref="Placement"/> fills a
/// fixed grid in reading order and wraps - on 1920x1080 that grid has <b>six</b> places (measured,
/// not reckoned) - so the 714 arrivals landed in six piles of about 119. The whole cost was paid
/// for pictures lying under other pictures.
/// </para>
/// <para>
/// <b>The bound is <see cref="AnimationBudget.ItemsPerScreen"/></b>, which Part 6 already states as
/// the number a screen is reckoned to carry at 1080p. Nothing enforced it until now; it was the
/// figure the display was measured against, and 714 is twenty-four times it. This is the one place
/// where a SINGLE action can pass it that far, so this is where it becomes a limit. Asking Part 6's
/// own number rather than choosing a second one means raising the budget raises this with it
/// (Guide <c>G24</c>).
/// </para>
/// <para>
/// <b>It bounds the RUN, not the table</b>, and that is the difference between a rule and an
/// obstruction. A quota on the screen would refuse the DM the thirty-first picture in the middle of
/// a session - the one refusal nobody would forgive. Bounding the run leaves every ordinary drop,
/// paste and drag exactly as it was: only a run that is itself larger than a screenful is cut, and
/// two folders dropped in a row are the DM doing it twice, on purpose.
/// </para>
/// <para>
/// <b>Nothing is lost, and that is what makes the cut affordable.</b> What is held back is in the
/// stock, where it was going anyway, and one drag from the panel puts any of it on the table. The
/// caller names the number - the same answer the background path already gives when a run of
/// several is meant as one background.
/// </para>
/// <para>
/// Unlike <see cref="Placement"/> this needs no scene and therefore does not have to be computed in
/// the hub: it is a bound on what the control SENDS, and a second control sending its own run is
/// bounded by its own count, which is the right answer for both.
/// </para>
/// </summary>
public static class Filling
{
    /// <summary>
    /// How many of <paramref name="arriving"/> pictures go onto the screen. The rest stay in the
    /// stock, and the caller says how many.
    /// </summary>
    public static int OntoTheScreen(int arriving) =>
        Math.Min(Math.Max(arriving, 0), AnimationBudget.ItemsPerScreen);
}
