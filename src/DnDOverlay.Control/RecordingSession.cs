using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// The session as the control's grips see it - the hub's, with the timeline in front of it (siting
/// question 3: the control remembers what it sent and sends the counter-move itself).
/// <para>
/// <b>The one type every grip goes through</b> (<c>C15</c>): which commands become a step is decided
/// here and nowhere else. Recorded are the acts that lay, take or move pictures - adding, removing,
/// moving, copying, a background set or cleared, a screen cleared, a scene or a layout loaded, "clear
/// all" and "send to all", each as ONE step however many screens it touches (Part 4). Everything else
/// passes straight through: movement, locking, parking, focus, names, layer switches, screen states.
/// A button that forgot the rule cannot exist, because no button decides it.
/// </para>
/// <para>
/// A step is the difference between the screens before and after the command (<see cref="TimelineStep"/>),
/// which is what makes the counter-move surgical. Recorded commands, undo and redo run one at a time,
/// or two snapshots taken around each other would mix their changes.
/// </para>
/// </summary>
internal sealed class RecordingSession : ISessionApi, IDisposable
{
    private readonly ISessionApi _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal RecordingSession(ISessionApi inner) => _inner = inner;

    /// <summary>Lets the gate go. The hub's session underneath belongs to the host and is not this one's to end.</summary>
    public void Dispose() => _gate.Dispose();

    /// <summary>The timeline - read by the buttons for their labels and by the delete question for its count.</summary>
    internal Timeline Timeline { get; } = new();

    /// <summary>The timeline changed - a step recorded, undone, redone, cut or cleared. Raised off the UI thread.</summary>
    internal event EventHandler? TimelineChanged;

    /// <summary>Takes back the last step. Returns its label, or none at the start of the session - no error.</summary>
    internal async Task<string?> UndoAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (Timeline.Undo() is not { } step)
            {
                return null;
            }

            await _inner.ApplyStepAsync(step.UndoOps()).ConfigureAwait(false);
            TimelineChanged?.Invoke(this, EventArgs.Empty);

            return step.Label;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Does the last undone step again. Returns its label, or none.</summary>
    internal async Task<string?> RedoAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (Timeline.Redo() is not { } step)
            {
                return null;
            }

            await _inner.ApplyStepAsync(step.RedoOps()).ConfigureAwait(false);
            TimelineChanged?.Invoke(this, EventArgs.Empty);

            return step.Label;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- recorded: what lays, takes or moves a picture

    public Task<ItemId> AddItemAsync(ScreenRef screen, AssetRef asset, Point? position, CancellationToken cancellationToken = default) =>
        RecordAsync([screen], _ => $"Add \"{asset.Name}\"", () => _inner.AddItemAsync(screen, asset, position, cancellationToken));

    public Task<IReadOnlyList<ItemId>> AddItemsAsync(ScreenRef screen, IReadOnlyList<AssetRef> assets, CancellationToken cancellationToken = default) =>
        RecordAsync([screen], _ => Pictures("Add", assets), () => _inner.AddItemsAsync(screen, assets, cancellationToken));

    public Task RemoveItemAsync(ScreenRef screen, ItemId item, CancellationToken cancellationToken = default) =>
        RecordAsync([screen], before => Items("Remove", before, screen, [item]), () => _inner.RemoveItemAsync(screen, item, cancellationToken));

    public Task RemoveItemsAsync(ScreenRef screen, IReadOnlyList<ItemId> items, CancellationToken cancellationToken = default) =>
        RecordAsync([screen], before => Items("Remove", before, screen, items), () => _inner.RemoveItemsAsync(screen, items, cancellationToken));

    public Task MoveItemAsync(ScreenRef source, ScreenRef target, ItemId item, Point? position, CancellationToken cancellationToken = default) =>
        RecordAsync(
            [source, target],
            before => $"{Items("Move", before, source, [item])} to {Label(target)}",
            () => _inner.MoveItemAsync(source, target, item, position, cancellationToken));

    public Task MoveItemsAsync(ScreenRef source, ScreenRef target, IReadOnlyList<ItemId> items, CancellationToken cancellationToken = default) =>
        RecordAsync(
            [source, target],
            before => $"{Items("Move", before, source, items)} to {Label(target)}",
            () => _inner.MoveItemsAsync(source, target, items, cancellationToken));

    public Task<ItemId?> CopyItemAsync(ScreenRef source, ScreenRef target, ItemId item, Point? position, CancellationToken cancellationToken = default) =>
        RecordAsync(
            [source, target],
            before => $"{Items("Copy", before, source, [item])} to {Label(target)}",
            () => _inner.CopyItemAsync(source, target, item, position, cancellationToken));

    public Task<IReadOnlyList<ItemId>> CopyItemsAsync(ScreenRef source, ScreenRef target, IReadOnlyList<ItemId> items, CancellationToken cancellationToken = default) =>
        RecordAsync(
            [source, target],
            before => $"{Items("Copy", before, source, items)} to {Label(target)}",
            () => _inner.CopyItemsAsync(source, target, items, cancellationToken));

    public Task SetBackgroundAsync(ScreenRef screen, AssetRef asset, CancellationToken cancellationToken = default) =>
        RecordAsync([screen], _ => $"Set background \"{asset.Name}\" on {Label(screen)}", () => _inner.SetBackgroundAsync(screen, asset, cancellationToken));

    public Task SetBackgroundOnAllAsync(AssetRef asset, CancellationToken cancellationToken = default) =>
        RecordAsync(null, _ => $"Set background \"{asset.Name}\" on all", () => _inner.SetBackgroundOnAllAsync(asset, cancellationToken));

    public Task ClearBackgroundAsync(ScreenRef screen, CancellationToken cancellationToken = default) =>
        RecordAsync([screen], _ => $"Remove background from {Label(screen)}", () => _inner.ClearBackgroundAsync(screen, cancellationToken));

    public Task ClearItemsAsync(ScreenRef screen, CancellationToken cancellationToken = default) =>
        RecordAsync([screen], _ => $"Clear {Label(screen)}", () => _inner.ClearItemsAsync(screen, cancellationToken));

    public Task<IReadOnlyList<ItemId>> LoadSceneAsync(ScreenRef screen, SceneState scene, SceneLoad mode, CancellationToken cancellationToken = default) =>
        RecordAsync([screen], _ => $"Load scene onto {Label(screen)}", () => _inner.LoadSceneAsync(screen, scene, mode, cancellationToken));

    public Task<IReadOnlyDictionary<ScreenRef, IReadOnlyList<ItemId>>> LoadLayoutAsync(
        IReadOnlyList<LayoutScene> scenes, LayoutLoad mode, CancellationToken cancellationToken = default) =>
        RecordAsync(null, _ => "Load layout", () => _inner.LoadLayoutAsync(scenes, mode, cancellationToken));

    public Task ClearAllAsync(CancellationToken cancellationToken = default) =>
        RecordAsync(null, _ => "Clear all", () => _inner.ClearAllAsync(cancellationToken));

    public Task<IReadOnlyDictionary<ScreenRef, IReadOnlyList<ItemId>>> SendToAllAsync(
        IReadOnlyList<AssetRef> assets, CancellationToken cancellationToken = default) =>
        RecordAsync(null, _ => $"{Pictures("Send", assets)} to all", () => _inner.SendToAllAsync(assets, cancellationToken));

    // ---- the two that change the timeline itself

    /// <summary>
    /// Clears every screen and empties the timeline (Part 4, <c>ResetSession</c>): behind a campaign
    /// switch lie pictures the hub can no longer deliver, and an undo would bring placeholders.
    /// </summary>
    public async Task ClearEverywhereAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _inner.ClearEverywhereAsync(cancellationToken).ConfigureAwait(false);
            Timeline.Clear();
            TimelineChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The first step of a delete from the stock: the timeline is cut first, so no undo can bring the
    /// pictures back (Part 4: only what is still in the stock comes back), then they leave the screens.
    /// The question before it named how many steps go, from <see cref="Timeline.CountLost"/>.
    /// </summary>
    public async Task RemoveAssetsAsync(IReadOnlyCollection<AssetId> assets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (Timeline.Truncate(assets.ToHashSet()) > 0)
            {
                TimelineChanged?.Invoke(this, EventArgs.Empty);
            }

            await _inner.RemoveAssetsAsync(assets, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- passed through: no picture laid, taken or moved

    public IReadOnlyList<ScreenView> Screens => _inner.Screens;

    public bool TouchPoints => _inner.TouchPoints;

    public IReadOnlyList<PendingPairing> PendingPairings => _inner.PendingPairings;

    public bool AcceptNewDevices
    {
        get => _inner.AcceptNewDevices;
        set => _inner.AcceptNewDevices = value;
    }

    public IReadOnlyList<RefusedDevice> RefusedDevices => _inner.RefusedDevices;

    public Task CallOrphansAsync(CancellationToken cancellationToken = default) => _inner.CallOrphansAsync(cancellationToken);

    public Task RequestDiagnosticsAsync(DeviceId device, bool wanted, CancellationToken cancellationToken = default) =>
        _inner.RequestDiagnosticsAsync(device, wanted, cancellationToken);

    public bool DiagnosticsRequested(DeviceId device) => _inner.DiagnosticsRequested(device);

    public Task<FocusOutcome> SetFocusAsync(ScreenRef screen, IReadOnlyList<ItemId> items, CancellationToken cancellationToken = default) =>
        _inner.SetFocusAsync(screen, items, cancellationToken);

    public Task RenameAssetAsync(AssetId asset, string name, CancellationToken cancellationToken = default) =>
        _inner.RenameAssetAsync(asset, name, cancellationToken);

    public Task ApplyStepAsync(IReadOnlyList<ScreenOp> ops, CancellationToken cancellationToken = default) =>
        _inner.ApplyStepAsync(ops, cancellationToken);

    public Task ParkItemsAsync(ScreenRef screen, IReadOnlyList<ItemId> items, bool parked, CancellationToken cancellationToken = default) =>
        _inner.ParkItemsAsync(screen, items, parked, cancellationToken);

    public Task SetItemsLockedAsync(ScreenRef screen, IReadOnlyList<ItemId> items, bool locked, CancellationToken cancellationToken = default) =>
        _inner.SetItemsLockedAsync(screen, items, locked, cancellationToken);

    public Task SetItemsShowNameAsync(ScreenRef screen, IReadOnlyList<ItemId> items, bool show, CancellationToken cancellationToken = default) =>
        _inner.SetItemsShowNameAsync(screen, items, show, cancellationToken);

    public Task SetItemsAnimationPausedAsync(ScreenRef screen, IReadOnlyList<ItemId> items, bool paused, CancellationToken cancellationToken = default) =>
        _inner.SetItemsAnimationPausedAsync(screen, items, paused, cancellationToken);

    public Task SetBackgroundFitAsync(ScreenRef screen, BackgroundFit fit, CancellationToken cancellationToken = default) =>
        _inner.SetBackgroundFitAsync(screen, fit, cancellationToken);

    public Task TurnBackgroundAsync(ScreenRef screen, int quarter, CancellationToken cancellationToken = default) =>
        _inner.TurnBackgroundAsync(screen, quarter, cancellationToken);

    public Task TransformBackgroundAsync(ScreenRef screen, Point centre, double scale, double rotationDeg, CancellationToken cancellationToken = default) =>
        _inner.TransformBackgroundAsync(screen, centre, scale, rotationDeg, cancellationToken);

    public Task SetAssetNameAsync(ScreenRef screen, AssetId asset, string name, CancellationToken cancellationToken = default) =>
        _inner.SetAssetNameAsync(screen, asset, name, cancellationToken);

    public Task SetShowNameAsync(ScreenRef screen, ItemId? item, bool show, CancellationToken cancellationToken = default) =>
        _inner.SetShowNameAsync(screen, item, show, cancellationToken);

    public Task SetAnimationPausedAsync(ScreenRef screen, ItemId? item, bool paused, CancellationToken cancellationToken = default) =>
        _inner.SetAnimationPausedAsync(screen, item, paused, cancellationToken);

    public Task ToggleItemsAsync(ScreenRef screen, bool visible, CancellationToken cancellationToken = default) =>
        _inner.ToggleItemsAsync(screen, visible, cancellationToken);

    public Task ToggleBackgroundAsync(ScreenRef screen, bool visible, CancellationToken cancellationToken = default) =>
        _inner.ToggleBackgroundAsync(screen, visible, cancellationToken);

    public Task TransformItemAsync(ScreenRef screen, ItemTransform transform, bool fromTable, bool toFront, bool binding, CancellationToken cancellationToken = default) =>
        _inner.TransformItemAsync(screen, transform, fromTable, toFront, binding, cancellationToken);

    public Task TransformItemsAsync(ScreenRef screen, IReadOnlyList<ItemTransform> transforms, bool toFront, CancellationToken cancellationToken = default) =>
        _inner.TransformItemsAsync(screen, transforms, toFront, cancellationToken);

    public Task SetLockedAsync(ScreenRef screen, ItemId item, bool locked, CancellationToken cancellationToken = default) =>
        _inner.SetLockedAsync(screen, item, locked, cancellationToken);

    public Task UnlockAllAsync(ScreenRef screen, CancellationToken cancellationToken = default) => _inner.UnlockAllAsync(screen, cancellationToken);

    public Task ParkItemAsync(ScreenRef screen, ItemId item, bool parked, CancellationToken cancellationToken = default) =>
        _inner.ParkItemAsync(screen, item, parked, cancellationToken);

    public Task RefitAsync(ScreenRef screen, CancellationToken cancellationToken = default) => _inner.RefitAsync(screen, cancellationToken);

    public Task<SceneState> GetSceneAsync(ScreenRef screen, CancellationToken cancellationToken = default) => _inner.GetSceneAsync(screen, cancellationToken);

    public ScreenContext ContextOf(ScreenRef screen) => _inner.ContextOf(screen);

    public DeviceSettings DeviceSettingsOf(DeviceId device) => _inner.DeviceSettingsOf(device);

    public Task SetScreenStateAsync(ScreenRef screen, ScreenState state, CancellationToken cancellationToken = default) =>
        _inner.SetScreenStateAsync(screen, state, cancellationToken);

    public Task SuppressAsync(ScreenRef screen, SuppressReason? reason, CancellationToken cancellationToken = default) =>
        _inner.SuppressAsync(screen, reason, cancellationToken);

    public Task ApplyConfigAsync(DeviceId device, ConfigUpdate update, CancellationToken cancellationToken = default) =>
        _inner.ApplyConfigAsync(device, update, cancellationToken);

    public Task SetTouchPointsAsync(bool reporting, CancellationToken cancellationToken = default) => _inner.SetTouchPointsAsync(reporting, cancellationToken);

    public Task SpotlightAsync(ScreenRef screen, Point at, CancellationToken cancellationToken = default) => _inner.SpotlightAsync(screen, at, cancellationToken);

    public Task IdentifyScreensAsync(DeviceId device, CancellationToken cancellationToken = default) => _inner.IdentifyScreensAsync(device, cancellationToken);

    public Task<bool> SendDeviceCommandAsync(DeviceId device, DeviceCommand command, string? name = null, CancellationToken cancellationToken = default) =>
        _inner.SendDeviceCommandAsync(device, command, name, cancellationToken);

    public IAsyncEnumerable<SessionEvent> Subscribe(CancellationToken cancellationToken = default) => _inner.Subscribe(cancellationToken);

    public Task ApprovePairingAsync(DeviceId device, string token, PairingRole role = PairingRole.Display, CancellationToken cancellationToken = default) =>
        _inner.ApprovePairingAsync(device, token, role, cancellationToken);

    public Task RejectAsync(DeviceId device, CancellationToken cancellationToken = default) => _inner.RejectAsync(device, cancellationToken);

    public Task<bool> AdoptDeviceAsync(DeviceId incoming, DeviceId known, string token, CancellationToken cancellationToken = default) =>
        _inner.AdoptDeviceAsync(incoming, known, token, cancellationToken);

    public Task<bool> ReassignScreenAsync(ScreenRef missing, ScreenRef present, CancellationToken cancellationToken = default) =>
        _inner.ReassignScreenAsync(missing, present, cancellationToken);

    public Task AcceptAsOwnDeviceAsync(DeviceId device, CancellationToken cancellationToken = default) => _inner.AcceptAsOwnDeviceAsync(device, cancellationToken);

    public Task UnpairAsync(DeviceId device, CancellationToken cancellationToken = default) => _inner.UnpairAsync(device, cancellationToken);

    public Task ClearRejectionAsync(DeviceId device, CancellationToken cancellationToken = default) => _inner.ClearRejectionAsync(device, cancellationToken);

    // ---- the recording itself

    private async Task RecordAsync(
        IReadOnlyCollection<ScreenRef>? screens, Func<IReadOnlyDictionary<ScreenRef, SceneState>, string> label, Func<Task> command) =>
        await RecordAsync(
            screens,
            label,
            async () =>
            {
                await command().ConfigureAwait(false);

                return true;
            }).ConfigureAwait(false);

    /// <summary>
    /// Runs one command between two pictures of the screens it can touch - the named ones, or every
    /// screen for a command over all of them - and records the difference as one step, if there is one.
    /// </summary>
    private async Task<T> RecordAsync<T>(
        IReadOnlyCollection<ScreenRef>? screens, Func<IReadOnlyDictionary<ScreenRef, SceneState>, string> label, Func<Task<T>> command)
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            var touched = screens ?? [.. _inner.Screens.Select(view => view.Screen)];
            var before = await PictureAsync(touched).ConfigureAwait(false);
            var result = await command().ConfigureAwait(false);
            var after = await PictureAsync(touched).ConfigureAwait(false);

            if (TimelineStep.Between(label(before), before, after) is { } step)
            {
                Timeline.Record(step);
                TimelineChanged?.Invoke(this, EventArgs.Empty);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<ScreenRef, SceneState>> PictureAsync(IReadOnlyCollection<ScreenRef> screens)
    {
        var picture = new Dictionary<ScreenRef, SceneState>();

        foreach (var screen in screens.Distinct())
        {
            picture[screen] = await _inner.GetSceneAsync(screen).ConfigureAwait(false);
        }

        return picture;
    }

    private string Label(ScreenRef screen) =>
        _inner.Screens.FirstOrDefault(view => view.Screen == screen)?.Info.Label ?? screen.Screen.Value;

    private static string Pictures(string verb, IReadOnlyList<AssetRef> assets) =>
        assets.Count == 1 ? $"{verb} \"{assets[0].Name}\"" : $"{verb} {assets.Count} pictures";

    private static string Items(string verb, IReadOnlyDictionary<ScreenRef, SceneState> before, ScreenRef screen, IReadOnlyList<ItemId> items)
    {
        if (items.Count != 1)
        {
            return $"{verb} {items.Count} pictures";
        }

        var named = before.GetValueOrDefault(screen)?.Items.OfType<ImageItem>().FirstOrDefault(item => item.ItemId == items[0]);

        return named is null ? $"{verb} picture" : $"{verb} \"{named.Name}\"";
    }
}
