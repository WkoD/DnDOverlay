using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// Takes the log entries one display forwards and puts them into the control's own log, so that
/// what the DM reads is ONE stream out of his own lines and everybody else's (Part 8).
/// <para>
/// Two things happen here that can only happen here, because they compare a device with us: the
/// second timestamp, and the notice when a device's clock disagrees with ours.
/// </para>
/// </summary>
internal sealed class LogRelay(
    ProcessLog? sink,
    TimeProvider time,
    ILogger logger,
    DeviceId device,
    string deviceName)
{
    /// <summary>
    /// How far apart two clocks may be before it is worth saying. Below a minute the difference
    /// explains nothing a reader would puzzle over; above it, the device's own diagnostic file
    /// reads wrong and somebody will eventually hold it in his hand (Part 8).
    /// </summary>
    private static readonly TimeSpan WorthSaying = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private long _windowStarted;
    private int _inWindow;
    private int _allowed = LimitAtInformation;
    private bool _clockReported;
    private bool _limitReported;

    /// <summary>
    /// The rate follows the LEVEL, because the documented way to look for a fault is to raise a
    /// display to Debug on purpose - a fixed rate would bite exactly when the DM asked for the
    /// flood (Part 4). Which level a device is set to shows in what it sends: a single entry below
    /// Information says it is looking for something.
    /// </summary>
    private const int LimitAtInformation = 20;

    private const int LimitAtDebug = 500;

    public void Take(LogEntryMessage entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var now = time.GetLocalNow();

        if (!Allowed(entry))
        {
            return;
        }

        NoteClock(entry, now);

        // Cleaned AGAIN here, on the way in. It was cleaned where it was written, but that was on
        // another machine: a device is trusted, not infallible, and a crafted name or exception
        // message must not be able to write lines of its own into the DM's file (Part 4).
        sink?.Add(new LogRecord(
            entry.At,
            now,
            entry.Level,
            entry.EventId,
            LogText.Clean(entry.EventName),
            [.. entry.Values.Select(value => new LogValue(LogText.Clean(value.Name), LogText.Clean(value.Text)))],
            LogText.Clean(entry.RawText) is { Length: > 0 } raw ? raw : null,
            new LogSource(device, deviceName),
            entry.Screen));
    }

    /// <summary>
    /// Keeps a device that has lost its mind from filling the control's log. Refused and reported,
    /// never swallowed - these limits keep the process alive, they are not access control
    /// (Part 4).
    /// </summary>
    private bool Allowed(LogEntryMessage entry)
    {
        if (_windowStarted == 0 || time.GetElapsedTime(_windowStarted) > Window)
        {
            _windowStarted = time.GetTimestamp();
            _inWindow = 0;
            _allowed = LimitAtInformation;
            _limitReported = false;
        }

        if (entry.Level < LogLevel.Information)
        {
            _allowed = LimitAtDebug;
        }

        _inWindow++;

        if (_inWindow <= _allowed)
        {
            return true;
        }

        if (!_limitReported)
        {
            _limitReported = true;
            HubLog.LogRateExceeded(logger, deviceName, _allowed, entry.Level);
        }

        return false;
    }

    /// <summary>
    /// Said once per connection, and only when it is worth saying. This is the one place in the
    /// protocol where an absolute foreign clock appears at all - and it is measured the moment it
    /// does, so there is no second one that could go unnoticed.
    /// </summary>
    private void NoteClock(LogEntryMessage entry, DateTimeOffset now)
    {
        if (_clockReported)
        {
            return;
        }

        _clockReported = true;

        var difference = entry.At - now;

        if (difference.Duration() > WorthSaying)
        {
            HubLog.DeviceClockDiffers(logger, deviceName, difference);
        }
    }
}
