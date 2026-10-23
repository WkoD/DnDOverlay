namespace DnDOverlay.Display;

/// <summary>
/// When the rescue mark stands and what it says (Part 6): up once the control has been gone for the
/// grace period - the same clock as the takeover window, so the mark is up exactly when this device
/// would answer a call (Part 4) - with the absence in whole minutes, never "0 min".
/// <para>
/// Its line is written <b>once per absence</b>, and only once there is an overlay to stand on: a
/// device with every screen inactive or windowed has nothing to rescue, and saying the mark is up
/// there would send somebody looking for it.
/// </para>
/// <para>
/// Asked once a second; the latch is the UI thread's.
/// </para>
/// </summary>
internal sealed class RescueMark
{
    private bool _up;

    /// <summary>
    /// How many minutes without a control the mark says, or <see langword="null"/> when it does not
    /// stand: the takeover window is shut, or this device was never connected in this run.
    /// </summary>
    /// <param name="due">Whether the grace period has run out (the takeover window is open).</param>
    /// <param name="absent">How long the control has been gone, if one ever was.</param>
    internal static int? Minutes(bool due, TimeSpan? absent) =>
        due && absent is { } gone ? Math.Max(1, (int)gone.TotalMinutes) : null;

    /// <summary>
    /// Whether this second is the one the mark comes up in - <see langword="true"/> once per absence,
    /// at the first second it stands on at least one overlay. A second without it ends the absence.
    /// </summary>
    /// <param name="standing">Whether the mark stands this second.</param>
    /// <param name="overlays">How many screens carry an overlay it is shown on.</param>
    internal bool CameUp(bool standing, int overlays)
    {
        if (standing && !_up && overlays > 0)
        {
            _up = true;

            return true;
        }

        if (!standing)
        {
            _up = false;
        }

        return false;
    }
}
