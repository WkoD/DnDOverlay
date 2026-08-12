using DnDOverlay.Core;

namespace DnDOverlay.Hub;

/// <summary>
/// The one place the authoritative state is changed, and at the same time the definition of what
/// <c>/ws/control</c> will translate (Part 4).
/// <para>
/// The line runs at the SCENE STATE, and it is narrower than it looks. What belongs here is
/// solely what changes or reads the authoritative arrangement. What does not: everything only
/// the DM sees - view rotation, tile order, hotkeys - and the entire stock, which belongs to
/// Campaign and is called by the control directly. That is why there is no <c>AddAsset</c> and
/// no <c>OpenCampaign</c> in here.
/// </para>
/// <para>
/// M1a implements the two members the running thread needs. The rest of the surface from Part 4
/// arrives with the milestones that serve it.
/// </para>
/// </summary>
public interface ISessionApi
{
    /// <summary>
    /// Puts an image on a screen. Placement, the width cap and the <c>ZOrder</c> happen HERE,
    /// not in the caller: placement means reading the state and writing it in the same breath,
    /// and two callers doing it at once would lay two images in the same slot (Part 3).
    /// </summary>
    /// <param name="position">
    /// An aimed drop point wins over the placement mode. <see langword="null"/> means "you
    /// decide".
    /// </param>
    Task<ItemId> AddItemAsync(
        ScreenRef screen,
        AssetRef asset,
        Point? position,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a scene - what "save screen as scene" will use.</summary>
    Task<SceneState> GetSceneAsync(ScreenRef screen, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every known screen with its reported facts, the DM's wish and whatever is getting in the
    /// way right now - the shape a tile is drawn from. Screens of a device that is switched off
    /// are in here too, and they are fully playable (Part 3).
    /// </summary>
    IReadOnlyList<ScreenView> Screens { get; }

    /// <summary>
    /// Sets the DM's wish for one screen. One of exactly two things that travel one way only, and
    /// the reason is in the model: all five states are born here, live in control.json and are
    /// never reported back (Part 3).
    /// </summary>
    Task SetScreenStateAsync(ScreenRef screen, ScreenState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or clears a transient FINDING - <see langword="null"/> means "nothing in the way".
    /// <para>
    /// It leaves the wish untouched, and that is the whole construction: there is nothing to
    /// remember and nothing to restore, so a screen unplugged while somebody changes its state
    /// comes back with the state that was set, not with the one that happened to be current when
    /// it went (Part 3).
    /// </para>
    /// </summary>
    Task SuppressAsync(
        ScreenRef screen,
        SuppressReason? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes display parameters from the control's side, as a DELTA - only the keys that
    /// changed.
    /// <para>
    /// It works while the device is switched off: what is set is kept and goes out with the next
    /// connection, which is what makes the device window usable before the display PC is even
    /// switched on (Part 4, Part 7).
    /// </para>
    /// </summary>
    Task ApplyConfigAsync(DeviceId device, ConfigUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything a surface shows, as one stream: the device tree, the scenes, the pairing desk and
    /// the log.
    /// <para>
    /// <b>Its own stream per call, not a shared one.</b> With two control devices a shared one
    /// would have the second taking the first one's events away (Part 4).
    /// </para>
    /// <para>
    /// <b>The first element is always a complete opening picture</b>
    /// (<see cref="SessionEvent.Opening"/>), and that is not a convenience for the caller. The hub
    /// is a hosted service and listens before the surface stands (rule 5): an autostarting display
    /// PC can connect, hand over its state and lodge a pairing request entirely before the first
    /// subscription exists. Without the opening picture the surface would see none of it and would
    /// wait for events that are long past.
    /// </para>
    /// <para>
    /// The stream ENDS rather than skipping when a subscriber falls too far behind - a state event
    /// is never dropped, so the only honest answer is to stop and let it subscribe again for a
    /// fresh picture. That is the same rule a socket follows (Part 4).
    /// </para>
    /// </summary>
    IAsyncEnumerable<SessionEvent> Subscribe(CancellationToken cancellationToken = default);

    /// <summary>
    /// What is knocking right now - never what knocked earlier. An open request has no deadline;
    /// it stands as long as its connection stands and vanishes with it (Part 4).
    /// </summary>
    IReadOnlyList<PendingPairing> PendingPairings { get; }

    /// <summary>
    /// The switch from Part 4, and it belongs with the device list and nowhere else: it acts on
    /// exactly what that list shows, and it is reached for in the moment the list is open anyway -
    /// when a strange device keeps knocking. With it off, a request is only logged (Part 7).
    /// </summary>
    bool AcceptNewDevices { get; set; }

    /// <summary>Devices that were turned away, with the reason and when they were last seen.</summary>
    IReadOnlyList<RefusedDevice> RefusedDevices { get; }

    /// <summary>
    /// Lets a waiting device in, with a token the caller has ALREADY encrypted and written.
    /// <para>
    /// The order is the promise, and it is why the token is a parameter rather than something
    /// this method makes up: the control creates it, protects it, saves control.json, and only
    /// then calls in here. The <c>Welcome</c> is sent from inside, so it cannot leave before the
    /// file exists - a control that died in between would otherwise leave a display holding a
    /// token nobody remembers (Part 7).
    /// </para>
    /// <para>
    /// <b>Barred over <c>/ws/control</c></b>, even with a valid control token: a control device
    /// may drive the session but not widen the circle of admitted devices, or one compromised
    /// tablet is enough to gain a permanent foothold (Part 4).
    /// </para>
    /// </summary>
    Task ApprovePairingAsync(
        DeviceId device,
        string token,
        PairingRole role = PairingRole.Display,
        CancellationToken cancellationToken = default);

    /// <summary>Says no. The device stays visible with its reason instead of simply vanishing.</summary>
    Task RejectAsync(DeviceId device, CancellationToken cancellationToken = default);

    /// <summary>
    /// For a device that turned out to be a clone: tells it to take a fresh identity and pair
    /// regularly. Also barred over <c>/ws/control</c> - it lets a device in (Part 4).
    /// </summary>
    Task AcceptAsOwnDeviceAsync(DeviceId device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws a token. In M5b this grows the confirmation with consequences, because it also
    /// discards this device's arrangements - the hub cannot address its screens afterwards
    /// (Part 3, Part 7).
    /// </summary>
    Task UnpairAsync(DeviceId device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a rejection back - the only way out of "rejected" the DM walks himself. Without it a
    /// mistaken "no" could only be healed at the device, with "reset pairing" on a machine that
    /// has no keyboard (Part 4).
    /// </summary>
    Task ClearRejectionAsync(DeviceId device, CancellationToken cancellationToken = default);
}
