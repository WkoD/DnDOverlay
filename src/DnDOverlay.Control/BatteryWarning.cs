using BatteryInfo = DnDOverlay.Core.BatteryInfo;

namespace DnDOverlay.Control;

/// <summary>
/// When the control warns about its own battery - the one real battery warning (DM, 19.10.2026, Part 7).
/// <para>
/// <b>Said once, at <see cref="BatteryInfo.LowPercent"/></b>, the line at which a display's gauge turns
/// red. It stands until the cable goes in or the battery is no longer low, and <b>comes again only after
/// the battery has been above the line in between</b> (or on the cable, or unknown) - a warning that
/// came back on every minute's look would be clicked away unread.
/// </para>
/// <para>
/// <see cref="ControlPower"/> looks once a minute and does what this answers: put the text in the box
/// beside the stage, or take it away again.
/// </para>
/// </summary>
internal sealed class BatteryWarning
{
    private bool _warned;

    /// <summary>What one look at the battery asks for.</summary>
    internal enum Step
    {
        /// <summary>Nothing changes.</summary>
        Nothing,

        /// <summary>The battery is low and has not been warned about since it was last above the line: say it.</summary>
        Warn,

        /// <summary>Not low, on the cable or unknown: a warning standing goes.</summary>
        Withdraw,
    }

    /// <summary>One look - <see langword="null"/> for a machine without a battery, or one that cannot be asked.</summary>
    internal Step Look(BatteryInfo? battery)
    {
        if (battery is null || battery.PluggedIn || !battery.Low)
        {
            // Back above the line or on the cable: the warning goes, and may come again next time.
            if (battery is null || battery.PluggedIn || battery.Percent > BatteryInfo.LowPercent)
            {
                _warned = false;
            }

            return Step.Withdraw;
        }

        if (_warned)
        {
            return Step.Nothing;
        }

        _warned = true;

        return Step.Warn;
    }
}
