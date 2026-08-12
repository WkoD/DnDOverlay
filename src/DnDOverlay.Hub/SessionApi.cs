using DnDOverlay.Core;
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
    private readonly ILogger<SessionApi> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private long _revision;

    public SessionApi(
        SceneStore scenes,
        ScreenCatalog screens,
        DisplayConnections connections,
        PairingDirectory pairing,
        ILogger<SessionApi> logger)
    {
        _scenes = scenes;
        _screens = screens;
        _connections = connections;
        _pairing = pairing;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<PendingPairing> PendingPairings => _pairing.Pending;

    /// <inheritdoc />
    public IReadOnlyList<RefusedDevice> RefusedDevices => _pairing.Refused;

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
            var scale = Layout.ScaleOnLoad(aspectRatio, context);

            // An aimed drop point wins; otherwise the placement mode of this screen decides.
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
            _connections.Dispatch(new ScenePatch([new ScreenOp(screen, op)]));

            return item.ItemId;
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

    public void Dispose() => _gate.Dispose();
}
