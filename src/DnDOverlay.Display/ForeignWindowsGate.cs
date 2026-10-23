using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// What this device does about the windows of other programs (Part 6): whether it reports them, and
/// whether it carries out a command for one.
/// <para>
/// <b>A command is refused before anything is touched</b> where the device only reports or reports
/// nothing, on a screen it does not have, and on an inactive screen - the DM handed that one back to
/// Windows, and somebody may be typing there.
/// </para>
/// </summary>
internal static class ForeignWindowsGate
{
    /// <summary>Whether the window list goes out: the control asked for it on this connection, and the device reports at all.</summary>
    internal static bool Reports(bool wanted, ForeignWindowsMode mode) => wanted && mode is not ForeignWindowsMode.Off;

    /// <summary>Why a command for a foreign window is refused, or <see langword="null"/> when it is carried out.</summary>
    /// <param name="screenKnown">Whether Windows offers the screen the command names.</param>
    /// <param name="inactive">Whether the DM set that screen inactive.</param>
    internal static WindowOutcome? Refusal(ForeignWindowsMode mode, bool screenKnown, bool inactive) =>
        mode is not ForeignWindowsMode.ReportAndControl ? WindowOutcome.NotAllowed
            : !screenKnown ? WindowOutcome.Gone
            : inactive ? WindowOutcome.Inactive
            : null;
}
