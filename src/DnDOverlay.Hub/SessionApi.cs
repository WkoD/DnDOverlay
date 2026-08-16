using System.Runtime.CompilerServices;
using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// The serialised implementation of <see cref="ISessionApi"/>. Every command runs to completion
/// before the next one starts, which is what makes "read the state and write it in the same
/// breath" safe - and placement is exactly that (Part 3).
/// </summary>
public sealed class SessionApi : ISessionApi, IDisposable
{
    private readonly SceneStore _scenes;
    private readonly ScreenCatalog _screens;
    private readonly DisplayConnections _connections;
    private readonly PairingDirectory _pairing;
    private readonly SessionEvents _events;
    private readonly ProcessLog? _log;
    private readonly ILogger<SessionApi> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private long _revision;

    public SessionApi(
        SceneStore scenes,
        ScreenCatalog screens,
        DisplayConnections connections,
        PairingDirectory pairing,
        SessionEvents events,
        ProcessLog? log,
        ILogger<SessionApi> logger)
    {
        _scenes = scenes;
        _screens = screens;
        _connections = connections;
        _pairing = pairing;
        _events = events;
        _log = log;
        _logger = logger;

        // Hooked up here rather than announced at each call site, and that is the difference
        // between a rule and a habit: a screen wish, a device arriving, a rejection taken back -
        // none of them can be published from somewhere that forgot to. Only the scene patch is
        // announced by hand, because only its caller holds the patch.
        // ViewChanged and not Changed: what is worth SHOWING is the wider of the two, and it is the
        // one a surface wants. Changed is the narrower "worth writing to control.json", and it is
        // the control's business rather than this one's (Part 3).
        _screens.ViewChanged += OnDevicesChanged;
        _connections.Changed += OnDevicesChanged;
        _pairing.Changed += OnPairingChanged;

        if (_log is not null)
        {
            _log.Added += OnLogged;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PendingPairing> PendingPairings => _pairing.Pending;

    /// <inheritdoc />
    public IReadOnlyList<RefusedDevice> RefusedDevices => _pairing.Refused;

    /// <inheritdoc />
    public bool AcceptNewDevices
    {
        get => _pairing.AcceptNewDevices;
        set => _pairing.AcceptNewDevices = value;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionEvent> Subscribe(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // BOTH locks, in the order every command takes them: the scene gate first, the fan-out
        // second. The fan-out's own lock keeps an event from slipping past the picture - it cannot
        // keep the picture from being taken between a command WRITING its scene and PUBLISHING its
        // patch, and that gap is the one that costs an item twice.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        SessionEvents.Subscription subscription;

        try
        {
            subscription = _events.Open(Opening);
        }
        finally
        {
            _gate.Release();
        }

        using var reading = subscription;

        await foreach (var @event in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return @event;
        }
    }

    /// <summary>
    /// Everything that stands right now, in one element. Assembled here because this is the one
    /// place that can see all four sources at once.
    /// </summary>
    private SessionEvent Opening() =>
        new SessionEvent.Opening(
            Devices(),
            [.. _scenes.Screens.Select(screen => (screen, _scenes.Get(screen)))],
            _pairing.Pending,
            _pairing.Refused);

    /// <summary>
    /// The device tree: every device the DM has allowed, with its screens underneath.
    /// <para>
    /// It is built from the PAIRED devices, not from whoever happens to be connected - a device
    /// that is switched off has to stay in the list with its screens, because its wishes and
    /// parameters live here and setting them before the display PC is even on is what the window
    /// is for (Part 7).
    /// </para>
    /// </summary>
    private IReadOnlyList<DeviceView> Devices()
    {
        var connected = _connections.All.ToDictionary(connection => connection.Device);

        var screens = _screens.Views()
            .GroupBy(view => view.Screen.Device)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ScreenView>)[.. group]);

        return
        [
            .. _pairing.Paired
                .Select(device => Compose(device, connected, screens))

                // A stable order, so a list that is rebuilt on every event does not reshuffle under
                // the DM's finger. By name, because that is what he reads.
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(device => device.Device.Value),
        ];
    }

    private static DeviceView Compose(
        PairedDevice device,
        Dictionary<DeviceId, DisplayConnection> connected,
        Dictionary<DeviceId, IReadOnlyList<ScreenView>> screens)
    {
        var here = connected.GetValueOrDefault(device.Device);

        return new DeviceView(
            device.Device,
            device.Name,
            here is not null,
            screens.GetValueOrDefault(device.Device, []),

            // Only while a socket is open. A version or a round trip remembered from last week
            // would read as current and would not be.
            here?.Address,
            here?.AppVersion,
            here?.ProtocolVersion,
            here?.RoundTrip);
    }

    private void OnDevicesChanged() => _events.Publish(new SessionEvent.DevicesChanged(Devices()));

    private void OnPairingChanged() =>
        _events.Publish(new SessionEvent.PairingChanged(_pairing.Pending, _pairing.Refused));

    private void OnLogged(LogRecord record) => _events.Publish(new SessionEvent.Logged(record));

    /// <inheritdoc />
    public async Task<ItemId> AddItemAsync(
        ScreenRef screen,
        AssetRef asset,
        Point? position,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var context = _screens.ContextFor(screen);
            var scene = _scenes.Get(screen);

            var aspectRatio = asset.Meta.AspectRatio;

            // Two bounds, composed, and each at the boundary it belongs to: how large a picture may
            // arrive on this SCREEN, and then how large it may be in the PLACE it is going to. The
            // second is what stopped a 7000×4211 picture overlapping its neighbours (hand-run of
            // M2b), and it does nothing at all in Cascade, which has no places.
            var scale = Placement.FitIntoItsPlace(
                Layout.ScaleOnLoad(aspectRatio, context), aspectRatio, context);

            // An aimed drop point wins; otherwise the placement mode of this screen decides. An
            // aimed one keeps the fitted size too - the DM chose the spot, not the size.
            var centre = position ?? Placement.NextPosition(scene, scale, aspectRatio, context);

            var item = new ImageItem(
                ItemId: new ItemId(Guid.NewGuid()),
                CenterX: centre.X,
                CenterY: centre.Y,
                Scale: scale,
                AspectRatio: aspectRatio,
                RotationDeg: context.DefaultRotationDeg,
                // What is touched comes to the front, and a new image counts as touched
                // (Part 3). The number space is per screen, which is why it is read from the
                // TARGET scene rather than carried along.
                ZOrder: scene.TopZOrder + 1,
                Locked: false,
                Parked: false,
                Revision: Interlocked.Increment(ref _revision),
                AssetId: asset.AssetId,
                Meta: asset.Meta,
                Name: asset.Name,
                ShowName: false,
                AnimationPaused: false);

            var op = new AddItem(item);

            _scenes.Set(screen, SceneReducer.Apply(scene, op, context));

            // One command, one patch - never merged with the next one over a time window
            // (Part 4). The store is written before the patch goes out, so a display that acts
            // on it can never be ahead of the hub.
            var patch = new ScenePatch([new ScreenOp(screen, op)]);

            _connections.Dispatch(patch);

            // The same patch to the surfaces. Built once and sent to both audiences, because a
            // second control has to APPLY it - handing it a whole scene instead would throw away
            // what patches are for (Part 4, rule 1).
            _events.Publish(new SessionEvent.ScenePatched(patch));

            return item.ItemId;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task RemoveItemAsync(ScreenRef screen, ItemId item, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new RemoveItem(item), cancellationToken);

    /// <inheritdoc />
    public Task SetBackgroundAsync(
        ScreenRef screen, AssetRef asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        // The finished layer travels, as the finished item does for AddItem: what the hub has
        // worked out is not worked out again at the other end (Part 1, rule 2). Fit and offset
        // start at their resting values - Cover, centred - and are moved from the thumbnail
        // afterwards (Part 6).
        var background = new BackgroundItem(
            asset.AssetId,
            asset.Meta,
            asset.Name,
            ShowName: false,
            Fit: BackgroundFit.Cover,
            OffsetX: 0,
            OffsetY: 0,
            AnimationPaused: false);

        return ApplyAsync(screen, new SetBackground(background), cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetBackgroundFitAsync(
        ScreenRef screen,
        BackgroundFit fit,
        double offsetX = 0,
        double offsetY = 0,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        BackgroundItem wanted;

        try
        {
            // Read and change under the same lock as the write, or two grips on the fit would
            // each compute from a background the other has already replaced.
            if (_scenes.Get(screen).Background is not { } background)
            {
                return;
            }

            wanted = background with { Fit = fit, OffsetX = offsetX, OffsetY = offsetY };
        }
        finally
        {
            _gate.Release();
        }

        await ApplyAsync(screen, new SetBackground(wanted), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ClearBackgroundAsync(ScreenRef screen, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new ClearBackground(), cancellationToken);

    /// <inheritdoc />
    public Task SetAssetNameAsync(
        ScreenRef screen, AssetId asset, string name, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new SetName(asset, name), cancellationToken);

    /// <inheritdoc />
    public Task SetShowNameAsync(
        ScreenRef screen, ItemId? item, bool show, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new SetShowName(item, show), cancellationToken);

    /// <inheritdoc />
    public Task SetAnimationPausedAsync(
        ScreenRef screen, ItemId? item, bool paused, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new SetAnimationPaused(item, paused), cancellationToken);

    /// <inheritdoc />
    public Task ToggleItemsAsync(ScreenRef screen, bool visible, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new ToggleItems(visible), cancellationToken);

    /// <inheritdoc />
    public Task ToggleBackgroundAsync(
        ScreenRef screen, bool visible, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new ToggleBackground(visible), cancellationToken);

    /// <summary>
    /// The shape every operation of M2b shares: apply to the authoritative scene under the lock,
    /// then send the very same patch to the devices and to the surfaces.
    /// <para>
    /// <b>The store is written before the patch goes out</b>, so a display acting on it can never
    /// be ahead of the hub. And it is <b>one</b> patch for both audiences, because a second control
    /// has to APPLY it - handing it a whole scene would throw away what patches are for (Part 4).
    /// </para>
    /// <para>
    /// <see cref="AddItemAsync"/> deliberately does not go through here: it computes placement, a
    /// ZOrder and a revision first, and those are the things that must not be duplicated. What is
    /// shared is the dispatch, not the decision.
    /// </para>
    /// </summary>
    private async Task ApplyAsync(ScreenRef screen, PatchOp op, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var context = _screens.ContextFor(screen);

            _scenes.Set(screen, SceneReducer.Apply(_scenes.Get(screen), op, context));

            var patch = new ScenePatch([new ScreenOp(screen, op)]);

            _connections.Dispatch(patch);
            _events.Publish(new SessionEvent.ScenePatched(patch));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SceneState> GetSceneAsync(
        ScreenRef screen,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _scenes.Get(screen);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScreenView> Screens => _screens.Views();

    /// <inheritdoc />
    public Task SetScreenStateAsync(
        ScreenRef screen,
        ScreenState state,
        CancellationToken cancellationToken = default)
    {
        // No gate: this touches the screen catalogue, which guards itself - not the scene state,
        // which is what the gate serialises.
        if (_screens.SetState(screen, state))
        {
            HubLog.ScreenStateChanged(_logger, screen, state);
            Push(screen.Device);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SuppressAsync(
        ScreenRef screen,
        SuppressReason? reason,
        CancellationToken cancellationToken = default)
    {
        if (_screens.SetSuppress(screen, reason))
        {
            if (reason is { } named)
            {
                HubLog.ScreenSuppressed(_logger, screen, named);
            }
            else
            {
                HubLog.ScreenAvailable(_logger, screen);
            }

            Push(screen.Device);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ApplyConfigAsync(
        DeviceId device,
        ConfigUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        foreach (var screen in update.Screens)
        {
            if (screen.Settings is { IsEmpty: false } settings)
            {
                _screens.Change(new ScreenRef(device, screen.Screen), settings);
            }

            if (screen.Command is { } command)
            {
                _ = _screens.SetState(new ScreenRef(device, screen.Screen), command.State);
                _ = _screens.SetSuppress(new ScreenRef(device, screen.Screen), command.Suppress);
            }
        }

        if (update.Device is { IsEmpty: false } settings_)
        {
            _screens.Change(device, settings_);
        }

        Push(device);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task IdentifyScreensAsync(DeviceId device, CancellationToken cancellationToken = default)
    {
        // No gate and no catalogue: this changes nothing that could be read back. A device that is
        // switched off gets nothing - unlike a setting, which is kept and goes out with the next
        // connection, an identification is only ever worth anything now.
        if (_connections.TryGet(device, out var connection))
        {
            _ = connection.TrySend(new IdentifyScreensMessage());
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends what is outstanding for this device - but only if it is here. What is not sent stays
    /// pending, which is the whole reason a screen can be set while its display PC is off
    /// (Part 7).
    /// </summary>
    internal void Push(DeviceId device)
    {
        if (!_connections.TryGet(device, out var connection))
        {
            return;
        }

        var update = _screens.Drain(device);

        if (!update.IsEmpty)
        {
            connection.TrySend(new ConfigUpdateMessage(update));
        }
    }

    /// <inheritdoc />
    public Task ApprovePairingAsync(
        DeviceId device,
        string token,
        PairingRole role = PairingRole.Display,
        CancellationToken cancellationToken = default)
    {
        // No gate here, and that is not an oversight: these five touch the pairing directory,
        // which guards itself - not the scene state, which is what the gate serialises.
        _ = _pairing.Approve(device, token, role);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RejectAsync(DeviceId device, CancellationToken cancellationToken = default)
    {
        _ = _pairing.Reject(device);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AcceptAsOwnDeviceAsync(DeviceId device, CancellationToken cancellationToken = default)
    {
        _ = _pairing.AcceptAsOwnDevice(device);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnpairAsync(DeviceId device, CancellationToken cancellationToken = default)
    {
        if (_pairing.Unpair(device))
        {
            HubLog.Unpaired(_logger, device);

            // The token is gone, so the next Hello would be refused anyway - but a connection
            // that is already open would carry on until then. Ending it makes "unpaired" mean
            // now rather than eventually.
            if (_connections.TryGet(device, out var connection))
            {
                connection.RequestClose();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearRejectionAsync(DeviceId device, CancellationToken cancellationToken = default)
    {
        if (_pairing.ClearRejection(device))
        {
            HubLog.RejectionCleared(_logger, device);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _screens.ViewChanged -= OnDevicesChanged;
        _connections.Changed -= OnDevicesChanged;
        _pairing.Changed -= OnPairingChanged;

        if (_log is not null)
        {
            _log.Added -= OnLogged;
        }

        _gate.Dispose();
    }
}
