using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// The screens the rescue mark appears on AND acts on - one rule for both, which is what keeps the
/// local hiding and the control's from ever overlapping (Part 6, Part 3): every screen with an
/// overlay, none in windowed mode. A window leaves the taskbar free, so there is nothing to rescue
/// there; a tap on the table's mark used to close the TV's window as well.
/// </summary>
internal static class RescueReach
{
    internal static List<ScreenId> Of(IEnumerable<(ScreenId Screen, bool Windowed)> windows) =>
        [.. windows.Where(window => !window.Windowed).Select(window => window.Screen)];
}
