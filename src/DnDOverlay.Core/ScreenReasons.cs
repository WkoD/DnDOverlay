namespace DnDOverlay.Core;

/// <summary>
/// Why nothing of a screen is to be seen at the table, though the DM did not ask for that - the
/// reasons mark in the head of a tile (Part 7).
/// <para>
/// <b>Five, and no more.</b> The last three are the findings of Part 3; the first two concern the
/// device and stand on every one of its tiles. What the DM set himself - the state, hidden layers,
/// a background, a focus - has its own switch below the thumbnail and is never a reason, or the mark
/// would glow on almost every played screen and be worn out within an evening.
/// </para>
/// </summary>
public enum ScreenReason
{
    /// <summary>The device is not paired with this control.</summary>
    NotPaired,

    /// <summary>The device is paired but not connected right now.</summary>
    DeviceNotConnected,

    /// <summary>The device is here, but this screen is not in its inventory.</summary>
    ScreenUnavailable,

    /// <summary>The control's own window lies on this screen.</summary>
    ControlWindow,

    /// <summary>Hidden at the device with the rescue mark.</summary>
    HiddenAtDevice,
}

/// <summary>The rule that turns what the hub knows into the reasons of one tile, in the order they are listed.</summary>
public static class ScreenReasons
{
    /// <summary>
    /// The reasons for this screen, most fundamental first: what concerns the device, then the
    /// screen's own finding. Both can stand at once - a device switched off while the control window
    /// lies on its screen has two reasons (step 32e). A device that is not here does not also call
    /// its screens unavailable: the hub reports that as <see cref="SuppressReason.Unavailable"/>
    /// too, and the cause is the device.
    /// </summary>
    /// <param name="device">The device the screen belongs to, or <see langword="null"/> when it is not paired.</param>
    public static IReadOnlyList<ScreenReason> Of(ScreenView screen, DeviceView? device)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var reasons = new List<ScreenReason>();
        var here = device is { Connected: true };

        if (device is null)
        {
            reasons.Add(ScreenReason.NotPaired);
        }
        else if (!device.Connected)
        {
            reasons.Add(ScreenReason.DeviceNotConnected);
        }

        switch (screen.Suppressed)
        {
            case SuppressReason.Unavailable when here:
                reasons.Add(ScreenReason.ScreenUnavailable);
                break;

            case SuppressReason.ControlWindow:
                reasons.Add(ScreenReason.ControlWindow);
                break;

            case SuppressReason.HiddenAtDevice:
                reasons.Add(ScreenReason.HiddenAtDevice);
                break;

            default:
                break;
        }

        return reasons;
    }

    /// <summary>
    /// The words for one reason, saying where it goes on - none of them can be mended from the tile
    /// (Part 7).
    /// </summary>
    public static string Words(ScreenReason reason) => reason switch
    {
        ScreenReason.NotPaired => "Not paired - pair it in the Devices window",
        ScreenReason.DeviceNotConnected => "Device not connected",
        ScreenReason.ScreenUnavailable => "Screen not available - check the cable",
        ScreenReason.ControlWindow => "The control window lies on it - move it away",
        ScreenReason.HiddenAtDevice => "Hidden at the device - comes back with the next update",
        _ => reason.ToString(),
    };

    /// <summary>The mark itself: symbol, count and word - "⚠ 2 reasons" - or nothing at all.</summary>
    public static string? Mark(IReadOnlyCollection<ScreenReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        return reasons.Count switch
        {
            0 => null,
            1 => "⚠ 1 reason",
            var count => $"⚠ {count} reasons",
        };
    }
}
