using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// One device's window lists on their way to the surfaces (Part 4): about one a second while asked
/// for, and held to the limits of the table there - ten lists a second per device, two hundred windows
/// a list. Beyond them it refuses rather than queues, and says so once per connection.
/// </summary>
internal sealed class WindowsRelay(
    SessionEvents events,
    TimeProvider time,
    ILogger logger,
    DeviceId device,
    string deviceName)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    /// <summary>Lists a second per device (Part 4, limits).</summary>
    internal const int Allowed = 10;

    /// <summary>Windows a list (Part 4) - a dozen visible ones is many.</summary>
    internal const int MostWindows = 200;

    private long _windowStarted;
    private int _inWindow;
    private bool _limitReported;

    internal void Take(WindowListMessage list)
    {
        if (!Allows())
        {
            return;
        }

        events.Publish(new SessionEvent.WindowList(device, [.. list.Windows.Take(MostWindows)]));
    }

    private bool Allows()
    {
        if (_windowStarted == 0 || time.GetElapsedTime(_windowStarted) > Window)
        {
            _windowStarted = time.GetTimestamp();
            _inWindow = 0;
        }

        _inWindow++;

        if (_inWindow <= Allowed)
        {
            return true;
        }

        if (!_limitReported)
        {
            _limitReported = true;
            HubLog.WindowListRateExceeded(logger, deviceName, Allowed);
        }

        return false;
    }
}
