using System.Runtime.InteropServices;
using System.Windows.Threading;
using DnDOverlay.Platform.Windows;
using Microsoft.Extensions.Logging;
using BatteryInfo = DnDOverlay.Core.BatteryInfo;

namespace DnDOverlay.Control;

/// <summary>
/// The control's own machine during a session: it must not fall asleep, and it must say when its
/// battery is about to take the hub with it (Part 7).
/// <para>
/// <b>Awake while a display is connected, and only then</b> (DM, 19.10.2026). Then the table hangs
/// on this hub; without a display the Surface may sleep as it always does, and preparing an evening
/// on the sofa is no reason to keep it up.
/// </para>
/// <para>
/// <b>The one real battery warning</b>, at the same 10 % at which a display's gauge turns red (DM,
/// 19.10.2026). A display's battery is only shown - the DM watches it fall. This one is said, in
/// the box beside the stage, because a Surface without power takes every display PC with it. It
/// stands until dismissed or until the cable goes in, and comes again only after the battery has
/// been above the line in between.
/// </para>
/// </summary>
internal sealed partial class ControlPower : IDisposable
{
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;

    private static readonly TimeSpan LookEvery = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private readonly Func<string, Action> _notify;
    private readonly Func<BatteryInfo?> _battery;
    private readonly DispatcherTimer _clock;

    private bool _holding;
    private Action? _withdraw;
    private bool _warned;

    /// <param name="notify">Puts a text in the box beside the stage and hands back how to take it away again.</param>
    internal ControlPower(ILogger logger, Func<string, Action> notify, Func<BatteryInfo?>? battery = null)
    {
        _logger = logger;
        _notify = notify;
        _battery = battery ?? Power.Battery;

        _clock = new DispatcherTimer { Interval = LookEvery };
        _clock.Tick += (_, _) => Look();
        _clock.Start();

        Look();
    }

    /// <summary>
    /// Whether any display is connected right now. Called on the UI thread - the execution state
    /// belongs to the thread that set it, and this one lives as long as the window.
    /// </summary>
    internal void Displays(bool connected)
    {
        if (connected == _holding)
        {
            return;
        }

        _holding = connected;
        _ = SetThreadExecutionState(connected ? Continuous | SystemRequired : Continuous);

        ControlLog.StandbyHeld(_logger, connected);
    }

    public void Dispose()
    {
        _clock.Stop();

        if (_holding)
        {
            _ = SetThreadExecutionState(Continuous);
            _holding = false;
        }
    }

    private void Look()
    {
        var battery = _battery();

        if (battery is null || battery.PluggedIn || !battery.Low)
        {
            // Back above the line or on the cable: the warning goes, and may come again next time.
            _withdraw?.Invoke();
            _withdraw = null;

            if (battery is null || battery.PluggedIn || battery.Percent > BatteryInfo.LowPercent)
            {
                _warned = false;
            }

            return;
        }

        if (_warned)
        {
            return;
        }

        _warned = true;
        ControlLog.BatteryLow(_logger, battery.Percent);

        _withdraw = _notify(Strings.PowerBatteryLow(battery.Percent));
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);
}
