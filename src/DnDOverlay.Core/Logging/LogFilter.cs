namespace DnDOverlay.Core.Logging;

/// <summary>How much of a device a filter shows: all of it, some of its screens, or nothing (Part 8, "half filled").</summary>
public enum Showing
{
    All,
    Partly,
    None,
}

/// <summary>
/// The log panel's filter bar (Part 8): which sources are shown. <b>A view, not a setting</b> - it
/// switches off nothing in the forwarding or the file, and it is not remembered across a start.
/// <para>
/// <b>Two levels, device and screen.</b> The device's bubble is the parent: a tap hides the device
/// with all its screens, a second brings everything back; a screen's bubble switches only itself, and
/// a device with some screens hidden stands half filled. A line without a screen belongs to its
/// device; a screen the control does not know falls back to its device rather than vanishing (Part 8).
/// </para>
/// </summary>
public sealed class LogFilter
{
    private readonly HashSet<DeviceId> _hiddenDevices = [];
    private readonly HashSet<(DeviceId Device, ScreenId Screen)> _hiddenScreens = [];

    /// <summary>Whether the control's own lines are hidden.</summary>
    public bool ControlHidden { get; private set; }

    /// <summary>Whether a line passes. <paramref name="known"/> says which screens of its device the control knows.</summary>
    public bool Shows(LogRecord record, Func<DeviceId, IReadOnlyCollection<ScreenId>> known)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(known);

        if (record.Source is not { } source)
        {
            return !ControlHidden;
        }

        if (_hiddenDevices.Contains(source.Device))
        {
            return false;
        }

        return record.Screen is not { } screen
            || !known(source.Device).Contains(screen)
            || !_hiddenScreens.Contains((source.Device, screen));
    }

    /// <summary>How much of a device is shown, for its bubble's fill.</summary>
    public Showing Of(DeviceId device, IReadOnlyCollection<ScreenId> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (_hiddenDevices.Contains(device))
        {
            return Showing.None;
        }

        return screens.Any(screen => _hiddenScreens.Contains((device, screen))) ? Showing.Partly : Showing.All;
    }

    /// <summary>Whether one screen of a device is shown.</summary>
    public bool Shows(DeviceId device, ScreenId screen) =>
        !_hiddenDevices.Contains(device) && !_hiddenScreens.Contains((device, screen));

    /// <summary>The device's bubble: hides the device with its screens when all of it is shown, brings everything back otherwise.</summary>
    public void ToggleDevice(DeviceId device, IReadOnlyCollection<ScreenId> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (Of(device, screens) is Showing.All)
        {
            _hiddenDevices.Add(device);
            return;
        }

        _hiddenDevices.Remove(device);
        _hiddenScreens.RemoveWhere(hidden => hidden.Device == device);
    }

    /// <summary>A screen's bubble switches only itself. On a hidden device it brings the device back with this one screen.</summary>
    public void ToggleScreen(DeviceId device, ScreenId screen, IReadOnlyCollection<ScreenId> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (_hiddenDevices.Remove(device))
        {
            foreach (var other in screens.Where(other => other != screen))
            {
                _hiddenScreens.Add((device, other));
            }

            _hiddenScreens.Remove((device, screen));
            return;
        }

        if (!_hiddenScreens.Remove((device, screen)))
        {
            _hiddenScreens.Add((device, screen));
        }
    }

    /// <summary>The control's own bubble.</summary>
    public void ToggleControl() => ControlHidden = !ControlHidden;

    /// <summary>
    /// "Only these" (Part 8): the one source shown, everything else hidden - a device with all its
    /// screens, one screen alone, or the control. <paramref name="screen"/> null means the whole device;
    /// <paramref name="device"/> null means the control.
    /// </summary>
    public void Only(DeviceId? device, ScreenId? screen, IReadOnlyDictionary<DeviceId, IReadOnlyCollection<ScreenId>> all)
    {
        ArgumentNullException.ThrowIfNull(all);

        ShowAll();
        ControlHidden = device is not null;

        foreach (var (other, screens) in all)
        {
            if (other != device)
            {
                _hiddenDevices.Add(other);
                continue;
            }

            if (screen is { } kept)
            {
                foreach (var hidden in screens.Where(candidate => candidate != kept))
                {
                    _hiddenScreens.Add((other, hidden));
                }
            }
        }
    }

    /// <summary>"Show all".</summary>
    public void ShowAll()
    {
        ControlHidden = false;
        _hiddenDevices.Clear();
        _hiddenScreens.Clear();
    }
}
