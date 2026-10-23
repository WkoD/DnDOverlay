using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Protocol;
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

    /// <summary>
    /// The names a device coming in must not carry: every OTHER device's name and every screen label
    /// of the other devices - device and screen names are unique across all devices (Part 3). The
    /// device's own screens are left out, because their default labels follow its name.
    /// </summary>
    internal static IReadOnlyList<string> TakenNames(IReadOnlyList<DeviceView> devices, DeviceId except)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var others = devices.Where(device => device.Device != except).ToList();

        return [.. others.Select(device => device.Name), .. others.SelectMany(device => device.Screens).Select(screen => screen.Info.Label)];
    }

    /// <summary>
    /// The name a device is paired under when its own is taken - <c>TISCH-PC (2)</c>, numbered the way
    /// the application names things where it cannot ask (Part 3). A cloned display PC boots with the
    /// original's machine name, and two devices called alike confuse the tiles, the target lists,
    /// the log bubbles and the layout matching.
    /// </summary>
    internal static string FreeName(string wanted, IEnumerable<string> taken) => FileNames.Numbered(wanted, taken);

    /// <summary>Lets a waiting device in: token to disk first, hub second.</summary>
    /// <param name="request">The device knocking.</param>
    /// <param name="name">
    /// The name it is paired under - its own, or a free one when that is taken (<see cref="FreeName"/>).
    /// A name other than its own is sent to the device as a rename, so its screens' labels follow it.
    /// </param>
    internal async Task AllowAsync(PendingPairing request, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var named = string.IsNullOrWhiteSpace(name) ? request.Name : name.Trim();
        var token = DeviceTokens.Create();

        _settings.Update(configuration => configuration with
        {
            KnownDevices =
            [
                .. configuration.KnownDevices.Where(device => device.DeviceId != request.Device.Value),
                new KnownDevice(
                    request.Device.Value,
                    named,
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

        // The device carries the name too: it is what it reports, and its screens' default labels
        // are built from it (Part 7). The same command as a rename in the device band.
        if (!string.Equals(named, request.Name, StringComparison.Ordinal))
        {
            _ = await _session.SendDeviceCommandAsync(request.Device, DeviceCommand.Rename, named).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Unpairs (Part 7): the device leaves <c>control.json</c> first, then the hub withdraws its token.
    /// <para>
    /// <b>Forgotten on disk, and first.</b> The entry used to stay: the hub took the token for this
    /// run, and the next start handed it back from <see cref="Restore"/> - the device was paired again
    /// without a question, and the unpair did not survive a restart (M5 review). Disk first for the
    /// reason an approval writes first: a control that died in between must not come back holding a
    /// pairing the DM took away.
    /// </para>
    /// </summary>
    internal async Task UnpairAsync(DeviceId device)
    {
        _settings.Update(configuration => configuration with
        {
            KnownDevices = [.. configuration.KnownDevices.Where(known => known.DeviceId != device.Value)],
        });

        _settings.Flush();

        await _session.UnpairAsync(device).ConfigureAwait(true);
    }

    /// <summary>
    /// Lets a waiting device in as a known one that is not connected (Part 7). The same order as an
    /// approval - token to disk first, hub second - and the control's half of the take-over: the known
    /// entry's name goes to the new one, its screens' tile places and view rotations follow the new
    /// identity. False when the hub refused, and then nothing here changed either.
    /// </summary>
    internal async Task<bool> AdoptAsync(PendingPairing request, DeviceView known)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(known);

        var before = _settings.Current;
        var token = DeviceTokens.Create();
        var from = known.Device.Value;
        var to = request.Device.Value;

        _settings.Update(configuration => configuration with
        {
            KnownDevices =
            [
                .. configuration.KnownDevices.Where(device => device.DeviceId != from && device.DeviceId != to),
                new KnownDevice(to, known.Name, PairingRole.Display, DeviceTokens.Store(_secrets, token), _time.GetLocalNow()),
            ],
            TileOrder = [.. configuration.TileOrder.Select(key => key.DeviceId == from ? key with { DeviceId = to } : key)],
            KnownScreens = [.. configuration.KnownScreens.Select(screen => screen.DeviceId == from ? screen with { DeviceId = to } : screen)],
        });

        _settings.Flush();

        if (await _session.AdoptDeviceAsync(request.Device, known.Device, token).ConfigureAwait(true))
        {
            return true;
        }

        _settings.Update(_ => before);
        _settings.Flush();

        return false;
    }

    /// <summary>
    /// The known devices a waiting one may be assigned to (Part 7): <b>only those that are not
    /// connected</b> - a connected one is demonstrably somebody else, and offering it would be the one
    /// wrong grip that throws two devices onto one entry - and never the waiting device itself.
    /// </summary>
    internal static IReadOnlyList<DeviceView> AdoptCandidates(IReadOnlyList<DeviceView> devices, PendingPairing request)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(request);

        return [.. devices.Where(device => !device.Connected && device.Device != request.Device)];
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
