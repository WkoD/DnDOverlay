using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Hub;

/// <summary>
/// One connected display, as the rest of the hub addresses it: a device, its screens, and a way
/// to put something on its socket.
/// <para>
/// The queues and the single writing loop live in <see cref="SendQueues"/>, and the heartbeat in
/// <see cref="Liveness"/> - both of which exist from the moment the socket is accepted, while this
/// only comes into being once the DM has let the device in. That split is the point: a connection
/// still waiting to be paired has no device to be addressed by, and yet it has to be written to
/// and watched.
/// </para>
/// </summary>
public sealed class DisplayConnection
{
    private readonly SendQueues _outgoing;
    private readonly Liveness _liveness;

    internal DisplayConnection(
        DeviceId device,
        IReadOnlyList<ScreenId> screens,
        HelloMessage hello,
        string address,
        SendQueues outgoing,
        Liveness liveness)
    {
        Device = device;
        Screens = [.. screens.Select(screen => new ScreenRef(device, screen))];
        Address = address;
        AppVersion = hello.AppVersion;
        ProtocolVersion = hello.ProtocolVersion;
        _outgoing = outgoing;
        _liveness = liveness;
    }

    public DeviceId Device { get; }

    /// <summary>
    /// Where this connection came from. Shown so a human can tell two devices apart, never used to
    /// identify one - it changes with DHCP and with the dock (Part 3).
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// What the device said about itself in its <c>Hello</c>. Both are reported and neither is
    /// enforced: a differing protocol version rejects nothing, because the control is the path
    /// along which a display gets updated (Part 4, Part 9).
    /// </summary>
    public string AppVersion { get; }

    /// <inheritdoc cref="AppVersion" />
    public int ProtocolVersion { get; }

    /// <summary>The last measured round trip, or <see langword="null"/> before the first answer.</summary>
    public TimeSpan? RoundTrip => _liveness.RoundTrip;

    /// <summary>
    /// The screens this connection is responsible for - the hub addresses per connection.
    /// <para>
    /// It changes while the connection stands: a monitor is plugged in, and the device says so
    /// with <c>ScreensChanged</c>. Without following that, a patch for the new screen would be
    /// sent to nobody - and the display's "unknown screen, discarded" net would never even see it
    /// (Part 4).
    /// </para>
    /// </summary>
    public IReadOnlyList<ScreenRef> Screens { get; private set; }

    /// <summary>Takes a new inventory. Called from the one loop that reads this socket.</summary>
    internal void Reported(IReadOnlyList<ScreenId> screens) =>
        Screens = [.. screens.Select(screen => new ScreenRef(Device, screen))];

    /// <summary>
    /// Fires when this connection is over - displaced by a newer one for the same device, silent
    /// for too long, or no longer taking what it is given.
    /// </summary>
    public CancellationToken Closing => _outgoing.Closing;

    /// <summary>
    /// Queues a message in the class it belongs to. Returns <see langword="false"/> when it could
    /// not be queued, which for a state message means this connection is already on its way out.
    /// </summary>
    public bool TrySend(ProtocolMessage message) => _outgoing.TrySend(message);

    /// <summary>
    /// Ends this connection because a newer one took over the device. The handler that owns it
    /// notices through <see cref="Closing"/> and cleans up - nobody reaches into another handler's
    /// socket.
    /// </summary>
    public void RequestClose() => _outgoing.RequestClose();

    /// <summary>Asks whether this connection is still there. See <see cref="Liveness.ProbeAsync"/>.</summary>
    internal Task<bool> ProbeAsync(TimeSpan grace, CancellationToken cancellationToken) =>
        _liveness.ProbeAsync(grace, cancellationToken);
}
