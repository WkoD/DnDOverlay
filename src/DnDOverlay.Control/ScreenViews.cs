using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control;

/// <summary>
/// How the DM looks at each screen on his stage - the view rotation, kept per screen in
/// <c>control.json</c> beside the inventory and nowhere else (Part 7).
/// <para>
/// <b>It is the control's own property</b>: nothing is sent to a device and nothing goes into the undo
/// timeline (Part 4). That is also why the hub's catalogue does not know it, and why every write of the
/// inventory has to carry it over rather than build the rows afresh - which is what reset it to zero at
/// every restart, twice at the table (hand-run of M4, 25).
/// </para>
/// </summary>
internal static class ScreenViews
{
    /// <summary>How the DM looks at this screen - unturned when the file knows nothing about it.</summary>
    internal static ViewRotation Of(ControlConfiguration configuration, ScreenRef screen) =>
        Find(configuration, screen) ?? ViewRotation.None;

    /// <summary>The kept view of this screen, or <see langword="null"/> when the file has no row for it.</summary>
    internal static ViewRotation? Find(ControlConfiguration configuration, ScreenRef screen)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.KnownScreens
            .FirstOrDefault(known => known.DeviceId == screen.Device.Value && known.ScreenId == screen.Screen.Value)
            ?.View;
    }

    /// <summary>
    /// The configuration with this screen turned. <b>A screen the file has never heard of gets its row
    /// here</b> rather than losing the value: until the hand-run the rotation was written only into an
    /// EXISTING row, so on a screen not yet named or configured it vanished without a word and came back
    /// at 0 after every restart (hand-run of M4, 25).
    /// </summary>
    /// <param name="view">The screen as the hub shows it - what a new row is built from; with none, no row is added.</param>
    internal static ControlConfiguration Turned(ControlConfiguration current, ScreenRef screen, ScreenView? view, ViewRotation rotation)
    {
        ArgumentNullException.ThrowIfNull(current);

        var written = false;

        var screens = current.KnownScreens
            .Select(known =>
            {
                if (known.DeviceId != screen.Device.Value || known.ScreenId != screen.Screen.Value)
                {
                    return known;
                }

                written = true;

                return known with { View = rotation };
            })
            .ToList();

        if (!written && view is { } seen)
        {
            screens.Add(new KnownScreen(
                screen.Device.Value,
                screen.Screen.Value,
                seen.Info.Label,
                seen.State,
                seen.Info.Size,
                seen.Info.Dpi,
                new ScreenSettings(),
                rotation));
        }

        return current with { KnownScreens = screens };
    }

    /// <summary>
    /// The inventory written back, with the views carried over (Part 3). The catalogue's rows come
    /// without one; a row whose screen the file knew keeps the view it had, a new row stays unturned,
    /// and a row the catalogue no longer has goes - view and all.
    /// </summary>
    internal static ControlConfiguration KeptThrough(ControlConfiguration configuration, IReadOnlyList<KnownScreen> inventory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(inventory);

        var seen = configuration.KnownScreens.ToDictionary(
            known => (known.DeviceId, known.ScreenId),
            known => known.View);

        return configuration with
        {
            KnownScreens =
            [
                .. inventory.Select(known =>
                    seen.TryGetValue((known.DeviceId, known.ScreenId), out var view)
                        ? known with { View = view }
                        : known),
            ],
        };
    }
}
