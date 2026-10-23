using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// Which foreign window a hand takes hold of on a tile, and by what (Part 7): the topmost window under
/// the place decides - <b>a window lying over the place hides the ones beneath it</b>, grip or not, so a
/// hand on the inside of the front window never reaches the strip of one behind it. A minimised window
/// is not on the screen and takes nothing.
/// <para>
/// Whether the device lets its windows be laid at all, and whether a picture covers the place, is
/// <see cref="WindowGrips"/>'s to ask before this.
/// </para>
/// </summary>
internal static class WindowPick
{
    /// <summary>The window and its grip at a place on the screen, or none.</summary>
    /// <param name="reach">How deep the strip and the corners reach, as a share of the screen.</param>
    internal static (ForeignWindow Window, WindowGrip Grip)? At(IReadOnlyList<ForeignWindow> windows, Point place, double reach)
    {
        ArgumentNullException.ThrowIfNull(windows);

        foreach (var window in windows.Where(window => !window.Minimized).OrderBy(window => window.ZOrder))
        {
            var grip = WindowGeometry.At(window.Bounds, place, reach);

            if (grip is not WindowGrip.None)
            {
                return (window, grip);
            }

            if (place.X >= window.Bounds.X && place.X <= window.Bounds.X + window.Bounds.Width
                && place.Y >= window.Bounds.Y && place.Y <= window.Bounds.Y + window.Bounds.Height)
            {
                return null;
            }
        }

        return null;
    }
}
