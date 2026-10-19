using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// Takes the diagnostics readings one display reports and passes them on, with a ceiling on how many
/// a device may send (Part 4).
/// <para>
/// The same shape as the touch ceiling and for the same reason: it keeps a device that has lost its
/// mind from spending the control's time, it is not access control. A reading comes about once a
/// second, so ten a second is an order of magnitude of room. Reported once per connection.
/// </para>
/// </summary>
internal sealed class DiagnosticsRelay(
    SessionEvents events,
    TimeProvider time,
    ILogger logger,
    DeviceId device,
    string deviceName)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    /// <summary>Ten a second per device (Part 4).</summary>
    internal const int Allowed = 10;

    private long _windowStarted;
    private int _inWindow;
    private bool _limitReported;

    internal void Take(DiagnosticsMessage report)
    {
        if (!Allows())
        {
            return;
        }

        events.Publish(new SessionEvent.Diagnostics(device, report.Reading));
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
            HubLog.DiagnosticsRateExceeded(logger, deviceName, Allowed);
        }

        return false;
    }
}
