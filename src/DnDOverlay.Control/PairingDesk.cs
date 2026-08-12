using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// Where a pairing decision turns into a token on disk.
/// <para>
/// The token is made HERE and not in the hub, and that is the one ordering that matters: the
/// control creates it, encrypts it, writes <c>control.json</c> - and only then tells the hub. The
/// <c>Welcome</c> is sent from inside <see cref="ISessionApi.ApprovePairingAsync"/>, so it cannot
/// possibly leave before the file exists. A control that died in between would otherwise leave a
/// display holding a token that nobody remembers, and the device would come back as a stranger
/// (Part 7).
/// </para>
/// </summary>
internal sealed class PairingDesk
{
    private readonly ISessionApi _session;
    private readonly ISecretStore _secrets;
    private readonly ControlSettings _settings;
    private readonly TimeProvider _time;

    internal PairingDesk(
        ISessionApi session,
        ISecretStore secrets,
        ControlSettings settings,
        TimeProvider time)
    {
        _session = session;
        _secrets = secrets;
        _settings = settings;
        _time = time;
    }

    /// <summary>
    /// Reads back what was paired before, for the snapshot the hub is built with.
    /// <para>
    /// A token that cannot be decrypted is DROPPED rather than fatal: it means the profile
    /// changed - a restored backup, a reinstalled Windows, a copied installation - and the answer
    /// to that is the same as to a missing token, namely that the device pairs again. That is
    /// what <see cref="ISecretStore.TryUnprotect"/> returning a value instead of throwing buys
    /// (Part 4).
    /// </para>
    /// </summary>
    internal static Restored Restore(ControlConfiguration configuration, ISecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var devices = new List<PairedDevice>();
        var dropped = 0;

        foreach (var known in configuration.KnownDevices)
        {
            if (DeviceTokens.TryRead(secrets, known.Token, out var token))
            {
                devices.Add(new PairedDevice(new DeviceId(known.DeviceId), known.Name, known.Role, token));
            }
            else
            {
                dropped++;
            }
        }

        return new Restored(devices, dropped);
    }

    /// <summary>Lets a waiting device in: token to disk first, hub second.</summary>
    internal async Task AllowAsync(PendingPairing request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = DeviceTokens.Create();

        _settings.Update(configuration => configuration with
        {
            KnownDevices =
            [
                .. configuration.KnownDevices.Where(device => device.DeviceId != request.Device.Value),
                new KnownDevice(
                    request.Device.Value,
                    request.Name,
                    PairingRole.Display,
                    DeviceTokens.Store(_secrets, token),
                    _time.GetLocalNow()),
            ],
        });

        // Past the debounce on purpose. Everywhere else it saves writes; here it would decide
        // whether the token survives the next five seconds (Part 6).
        _settings.Flush();

        // The hub writes the log line for this (event 1021). A second one from here would say the
        // same thing twice - and the range that owns it is connection, not operations (Part 8).
        await _session.ApprovePairingAsync(request.Device, token).ConfigureAwait(true);
    }

    /// <summary>Says no. The device keeps its place in the list, with the reason (Part 4).</summary>
    internal Task DenyAsync(PendingPairing request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _session.RejectAsync(request.Device);
    }

    /// <summary>
    /// For a clone: tells it to take a fresh identity and pair regularly. Cloning a disk is the
    /// usual way to set up a second display PC, so this is a normal path and not an incident
    /// (Part 7).
    /// </summary>
    internal Task AcceptAsOwnAsync(PendingPairing request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _session.AcceptAsOwnDeviceAsync(request.Device);
    }

    /// <summary>What came back out of control.json, and how much of it was unreadable.</summary>
    internal sealed record Restored(IReadOnlyList<PairedDevice> Devices, int Dropped);
}
