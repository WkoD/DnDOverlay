using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// "Take over" (Part 3, "reassign screen"), the control's half: a screen that is there takes over a
/// missing one - the hub moves its wish, settings, name and arrangement, and the control moves what only
/// it keeps, the tile place and the view rotation. The old entry goes.
/// <para>
/// <b>The old view is read BEFORE the hub is asked.</b> The hub's reassign announces the changed
/// inventory, and the control writes the inventory back on that announcement at once
/// (<see cref="ScreenViews.KeptThrough"/>) - without the old screen, whose row goes with its view. Read
/// afterwards, there was no view left to carry over, and the new screen stayed unturned.
/// </para>
/// </summary>
internal static class ScreenTakeOver
{
    /// <summary>Asks the hub, then moves the tile place and the view. False when the hub refused - and then nothing here changed.</summary>
    internal static async Task<bool> RunAsync(ISessionApi session, ControlSettings settings, ScreenRef old, ScreenRef fresh)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);

        var view = ScreenViews.Find(settings.Current, old);

        if (!await session.ReassignScreenAsync(old, fresh).ConfigureAwait(true))
        {
            return false;
        }

        settings.Update(configuration => Inherited(configuration, old, fresh, view));

        return true;
    }

    /// <summary>
    /// The configuration after the take-over: the new screen stands in the tile order where the old one
    /// stood - and only there - and gets the old one's view; the old row goes.
    /// </summary>
    /// <param name="view">The old screen's view as it was before the hub was asked, or none when the file had no row for it.</param>
    internal static ControlConfiguration Inherited(ControlConfiguration configuration, ScreenRef old, ScreenRef fresh, ViewRotation? view)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var from = new ScreenKey(old.Device.Value, old.Screen.Value);
        var to = new ScreenKey(fresh.Device.Value, fresh.Screen.Value);

        var order = configuration.TileOrder.Contains(from)
            ? configuration.TileOrder.Where(key => key != to).Select(key => key == from ? to : key)
            : configuration.TileOrder;

        return configuration with
        {
            TileOrder = [.. order],
            KnownScreens =
            [
                .. configuration.KnownScreens
                    .Where(known => !(known.DeviceId == from.DeviceId && known.ScreenId == from.ScreenId))
                    .Select(known => known.DeviceId == to.DeviceId && known.ScreenId == to.ScreenId && view is { } turned ? known with { View = turned } : known),
            ],
        };
    }
}
