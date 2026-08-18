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
    /// <summary>
    /// Takes one image off a screen. An <c>ItemId</c> the screen does not carry is not an error -
    /// a command may reach the hub after the item is already gone (Part 11).
    /// </summary>
    Task RemoveItemAsync(ScreenRef screen, ItemId item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a picture on the background layer of a screen, replacing whatever was there.
    /// <see cref="ClearBackgroundAsync"/> is the counterpart, and it is a separate call rather than
    /// this one with a null: the two are strictly separate operations, which is what makes "empty
    /// the lot" have to say both out loud (Part 3).
    /// </summary>
    Task SetBackgroundAsync(ScreenRef screen, AssetRef asset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes how the background sits without touching which picture it is: the fit, and which
    /// part of the crop is seen.
    /// <para>
    /// There is no operation of its own for this - the background travels as a whole layer, so
    /// changing it is a read, a change and a <c>SetBackground</c> under the same lock. A screen
    /// with no background is left alone rather than given an empty one.
    /// </para>
    /// </summary>
    Task SetBackgroundFitAsync(
        ScreenRef screen,
        BackgroundFit fit,
        double offsetX = 0,
        double offsetY = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Takes the background layer away, leaving the items where they are.</summary>
    Task ClearBackgroundAsync(ScreenRef screen, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an ASSET wherever it shows on this screen - every item carrying it and the
    /// background too. "One picture, one name" (Part 3): the control sends one of these per
    /// affected screen, otherwise the same picture would briefly be called two different things.
    /// </summary>
    Task SetAssetNameAsync(
        ScreenRef screen, AssetId asset, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether one item wears its caption. <paramref name="item"/> <see langword="null"/> means the
    /// background layer, which has the same field - a city map wants to be able to carry its name
    /// (Part 7).
    /// </summary>
    Task SetShowNameAsync(
        ScreenRef screen, ItemId? item, bool show, CancellationToken cancellationToken = default);

    /// <summary>Holds one animation still, or lets it run again. <see langword="null"/> means the background.</summary>
    Task SetAnimationPausedAsync(
        ScreenRef screen, ItemId? item, bool paused, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the item layer of this screen is drawn. It hides rather than deletes - the pictures
    /// stay in the scene and in the device's store, which is what makes fading them back in
    /// immediate and free of a second transfer (Part 7).
    /// </summary>
    Task ToggleItemsAsync(ScreenRef screen, bool visible, CancellationToken cancellationToken = default);

    /// <summary>The same for the background layer, independent of the items in all four combinations.</summary>
    Task ToggleBackgroundAsync(ScreenRef screen, bool visible, CancellationToken cancellationToken = default);

    Task<ItemId> AddItemAsync(
        ScreenRef screen,
        AssetRef asset,
        Point? position,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an item to where it now lies. Everything the sender may not decide happens here: the
    /// position is held at the edge, the scale between its bounds, and the revision and the
    /// <c>ZOrder</c> are handed out (Part 4).
    /// </summary>
    /// <param name="fromTable">
    /// Whether a hand at the table did this. It is the one thing that decides the lock: the lock
    /// guards against the TABLE and not against the DM, who would otherwise have to unlock before
    /// every correction (Part 3). A refused transform is logged and changes nothing.
    /// </param>
    /// <param name="toFront">
    /// Whether this was a GRAB rather than a command - the first report of a gesture, and from M4
    /// the moment the DM takes hold in the thumbnail. What is touched comes to the front; a locked
    /// item never does, because it cannot be taken hold of (Part 3).
    /// </param>
    Task TransformItemAsync(
        ScreenRef screen,
        ItemTransform transform,
        bool fromTable,
        bool toFront,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks one item against gestures at the table, or releases it. Nothing moves, and nothing
    /// enters the undo timeline - the worst outcome of an unlock is that the DM sets a few
    /// padlocks again (Part 3).
    /// </summary>
    Task SetLockedAsync(ScreenRef screen, ItemId item, bool locked, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases every locked item of ONE screen, in a single patch.
    /// <para>
    /// Over an evening the DM locks a handful of pictures and the players eventually report "that
    /// one does not work". Going through twelve items one at a time is the wrong answer to that;
    /// one grip releases them all, and the padlock visible on each item is what makes the sweep
    /// harmless - whoever can see which five were locked can tap them back in seconds (Part 3).
    /// </para>
    /// </summary>
    Task UnlockAllAsync(ScreenRef screen, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lays an item into the slot bar along the park edge, or takes it out again. Size and
    /// rotation survive both ways - the Java version reset them and undid the work of lining a
    /// picture up (Part 6). Coming back out brings the item to the front.
    /// </summary>
    Task ParkItemAsync(ScreenRef screen, ItemId item, bool parked, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fits the scene of one screen to the screen as it is NOW - after a resolution change, a
    /// rotation, or a device coming back with a monitor that is no longer the one it left with.
    /// <para>
    /// It is the one command nobody gives. Everything else here answers a grip of the DM's or a
    /// hand at the table; this answers a fact of the hardware, and it has to exist because the
    /// bounds it enforces are expressed in the screen's own terms: <c>MinScale</c> in DIP of its
    /// height, the graspable remainder in DIP of both its edges. A table switched from 1080p to
    /// something smaller would otherwise keep pictures that are now too small to hit and slivers
    /// that are now off the edge (Part 11).
    /// </para>
    /// <para>
    /// It leaves no step in the undo timeline, for the same reason no transformation does: undo is
    /// for what the DM did, and nobody did this (Part 3).
    /// </para>
    /// </summary>
    Task RefitAsync(ScreenRef screen, CancellationToken cancellationToken = default);

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
    /// Makes every overlay of one device show its own name, large, for a few seconds (Part 6).
    /// <para>
    /// With two devices of two screens each there is otherwise nothing that says which tile is
    /// which physical screen - the names are the DM's own and the identifiers behind them never
    /// appear in any surface (Part 3). It is a moment, not a state: nothing is stored, nothing is
    /// undone, and a device that is not connected is simply not asked.
    /// </para>
    /// </summary>
    Task IdentifyScreensAsync(DeviceId device, CancellationToken cancellationToken = default);

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
