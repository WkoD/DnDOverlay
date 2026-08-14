using System.Net;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DnDOverlay.Core;
using DnDOverlay.Hub;
using DnDOverlay.Platform.Windows;

namespace DnDOverlay.Control;

/// <summary>
/// Keeps the control's own window from being covered by its own overlay.
/// <para>
/// A screen the control window lies on gets no overlay - otherwise the always-on-top window of
/// this application lands on the DM's stage and he cannot see what he is arranging. That makes the
/// one-machine arrangement a regular one rather than a special mode (Part 2).
/// </para>
/// <para>
/// It is a FINDING, never a state: <see cref="SuppressReason.ControlWindow"/> travels beside the
/// wish and leaves it untouched, so when the window moves away the screen is simply played on
/// again - with the state that stood there all along. There is nothing to remember and nothing to
/// restore, which is exactly why findings are not states (Part 3).
/// </para>
/// </summary>
internal sealed class StageGuard : IDisposable
{
    /// <summary>
    /// Dragging a window fires by the pixel. Without this every step would be a round trip to the
    /// hub and a ConfigUpdate to the device; with it, one at the end of the drag.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    private readonly Window _window;
    private readonly ISessionApi _session;
    private readonly string _deviceName;
    private readonly DispatcherTimer _debounce;

    /// <summary>What we have set, so that clearing only ever undoes our own finding.</summary>
    private readonly HashSet<ScreenRef> _blocked = [];

    private IReadOnlyList<DeviceView> _devices = [];
    private bool _disposed;

    internal StageGuard(Window window, ISessionApi session, string deviceName)
    {
        _window = window;
        _session = session;
        _deviceName = deviceName;

        _debounce = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = Settle,
        };

        _debounce.Tick += (_, _) => Recompute();

        _window.LocationChanged += OnMoved;
        _window.SizeChanged += OnMoved;
        _window.StateChanged += OnMoved;
        _window.DpiChanged += OnMoved;

        // Coming to the front is the one change that must NOT wait out the debounce. Whatever way
        // back the DM took - Alt+Tab, the taskbar, starting the control a second time - his window
        // arrives UNDERNEATH an always-on-top overlay, so for as long as this waits he is looking
        // at a screen that does not show what he just asked for. Everything else may settle.
        _window.Activated += OnRaised;
    }

    /// <summary>
    /// The device list, from the surface's own subscription rather than a second one. It is an
    /// input like the window position: a device connecting or leaving changes the answer, so it
    /// restarts the same debounce.
    /// </summary>
    internal void Knows(IReadOnlyList<DeviceView> devices)
    {
        _devices = devices;
        OnMoved(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _window.LocationChanged -= OnMoved;
        _window.SizeChanged -= OnMoved;
        _window.StateChanged -= OnMoved;
        _window.DpiChanged -= OnMoved;
        _window.Activated -= OnRaised;

        _debounce.Stop();

        // Let go on the way out. The finding is transient by construction - the hub drops it when
        // the device goes - but a control that is closing has no window on anything any more, and
        // saying so costs one message.
        foreach (var screen in _blocked.ToList())
        {
            _ = _session.SuppressAsync(screen, reason: null);
        }

        _blocked.Clear();
    }

    private void OnMoved(object? sender, EventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnRaised(object? sender, EventArgs e) => Recompute();

    private void Recompute()
    {
        _debounce.Stop();

        if (_disposed)
        {
            return;
        }

        var covered = Covered();
        var wanted = new HashSet<ScreenRef>();

        // Asked afresh, like the monitors: docking changes both, and a cached answer would be the
        // one that was true before the dock.
        var own = LocalAddresses.Enumerate();

        foreach (var device in _devices.Where(device => Here(device, own)))
        {
            foreach (var screen in device.Screens.Where(screen => covered.Contains(screen.Screen.Screen)))
            {
                _ = wanted.Add(screen.Screen);
            }
        }

        foreach (var screen in wanted.Where(screen => !_blocked.Contains(screen)))
        {
            _ = _session.SuppressAsync(screen, SuppressReason.ControlWindow);
        }

        foreach (var screen in _blocked.Where(screen => !wanted.Contains(screen)).ToList())
        {
            _ = _session.SuppressAsync(screen, reason: null);
        }

        _blocked.Clear();
        _blocked.UnionWith(wanted);
    }

    private HashSet<ScreenId> Covered()
    {
        var handle = new WindowInteropHelper(_window).Handle;

        // The enumeration is asked afresh rather than kept: a DPI change and a hot-plug both land
        // here, and a cached list would be the one that was true before it happened.
        return [.. StageCover.CoveredBy(handle, Screens.Enumerate(_deviceName))];
    }

    /// <summary>
    /// Whether that device is on THIS machine, and the connection is the only thing that can say
    /// so. A machine name would not do - it is renameable and, on cloned display PCs, identical;
    /// a <see cref="ScreenId"/> would not either, because two machines from one disk image can
    /// report the very same device instance path (Part 3). Without this the control would suppress
    /// a stranger's table because its monitor happens to carry the same path as ours.
    /// <para>
    /// <b>Not the loopback interface</b>, although the plan said so - measured, a display on this
    /// very machine connects to the LAN address, because it takes whichever beacon it hears first
    /// and the beacon goes out on every interface. The rule that holds is the wider one, and it is
    /// no weaker: an address this machine answers on cannot belong to another machine.
    /// </para>
    /// </summary>
    private static bool Here(DeviceView device, IReadOnlyList<LocalAddress> own) =>
        device.Connected && LocalAddresses.IsThisMachine(device.Address, own);
}
