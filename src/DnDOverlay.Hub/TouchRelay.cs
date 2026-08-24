using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// Takes the touch points one display reports and passes them on to whoever is watching, with a
/// ceiling on how many a device may send.
/// <para>
/// The ceiling keeps a device that has lost its mind from spending the control's time; it is not
/// access control, and what it protects is the process rather than the data (Part 4). It is
/// generous by design: ten a second per screen is the rate, eight screens is the most a device may
/// report, so an ordinary table sits an order of magnitude below it.
/// </para>
/// <para>
/// Reported <b>once</b> for the whole connection. A line per refusal at this rate would be the
/// flood it is complaining about, and a line per second would still be one - unlike the log rate,
/// which a DM raises on purpose and may want to see bite again after he lowers it.
/// </para>
/// </summary>
internal sealed class TouchRelay(
    SessionEvents events,
    TimeProvider time,
    ILogger logger,
    string deviceName)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ten a second per screen is the rate, and a device may report eight screens (Part 4). Twice
    /// that leaves room for a burst that catches up after a stall without touching the ceiling.
    /// </summary>
    private const int Allowed = 160;

    private long _windowStarted;
    private int _inWindow;
    private bool _limitReported;

    /// <summary>
    /// Passes one report on, or refuses it. The screen is checked by the caller, which is the only
    /// place that knows which screens this socket is addressed by.
    /// </summary>
    internal void Take(ScreenRef screen, TouchPointsMessage report)
    {
        if (!Allows())
        {
            return;
        }

        events.Publish(new SessionEvent.TouchPoints(screen, report.Touches));
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
            HubLog.TouchRateExceeded(logger, deviceName, Allowed);
        }

        return false;
    }
}
