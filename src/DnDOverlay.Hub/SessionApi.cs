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
                Revision: _scenes.NextRevision(),
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
        // worked out is not worked out again at the other end (Part 1, rule 2). A fresh background
        // arrives covering the screen, and is moved from the thumbnail afterwards (Part 6).
        var (centre, scale) = Layout.FitBackground(
            asset.Meta.AspectRatio, BackgroundFit.Cover, _screens.ContextFor(screen));

        var background = new BackgroundItem(
            asset.AssetId,
            asset.Meta,
            asset.Name,
            ShowName: false,
            CenterX: centre.X,
            CenterY: centre.Y,
            Scale: scale,
            RotationDeg: 0,
            AnimationPaused: false);

        return ApplyAsync(screen, new SetBackground(background), cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetBackgroundFitAsync(
        ScreenRef screen,
        BackgroundFit fit,
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

            var (centre, scale) = Layout.FitBackground(
                background.Meta.AspectRatio, fit, _screens.ContextFor(screen));

            wanted = background with { CenterX = centre.X, CenterY = centre.Y, Scale = scale };
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
    private Task ApplyAsync(ScreenRef screen, PatchOp op, CancellationToken cancellationToken) =>
        ApplyAsync(screen, [op], cancellationToken);

    /// <summary>
    /// Several operations of ONE command, in one patch. That is what "unlock all" needs and what
    /// the loading of a layout will need in M5b: one command, one patch, one step in the timeline -
    /// a half-rebuilt table must never become visible (Part 4).
    /// </summary>
    private async Task ApplyAsync(
        ScreenRef screen,
        IReadOnlyList<PatchOp> ops,
        CancellationToken cancellationToken)
    {
        if (ops.Count == 0)
        {
            // Nothing to say. A patch with no operations would still be a revision and a step in
            // the timeline, and "unlock all" on a screen with nothing locked is the normal way to
            // get here.
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var context = _screens.ContextFor(screen);
            var scene = _scenes.Get(screen);

            foreach (var op in ops)
            {
                scene = SceneReducer.Apply(scene, op, context);
            }

            _scenes.Set(screen, scene);

            var patch = new ScenePatch([.. ops.Select(op => new ScreenOp(screen, op))]);

            _connections.Dispatch(patch);
            _events.Publish(new SessionEvent.ScenePatched(patch));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task MoveItemAsync(
        ScreenRef source,
        ScreenRef target,
        ItemId item,
        Point? position,
        CancellationToken cancellationToken = default)
    {
        if (source == target)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var lying = _scenes.Get(source);

            if (lying.Items.FirstOrDefault(candidate => candidate.ItemId == item) is not { } current)
            {
                return;
            }

            var context = _screens.ContextFor(target);
            var scene = _scenes.Get(target);
            var revision = _scenes.NextRevision();

            var removal = new RemoveItem(item);
            var addition = new AddItem(Arriving(current, scene, context, position, revision));

            _scenes.Set(source, SceneReducer.Apply(lying, removal, _screens.ContextFor(source)));
            _scenes.Set(target, SceneReducer.Apply(scene, addition, context));

            // ONE patch over two screens. Both halves reach every display: the one losing the
            // picture and the one gaining it are usually different devices, and the arrival
            // highlight reads the ops of ITS screen - a plain AddItem on the target, a plain
            // RemoveItem on the source (Arrival).
            var patch = new ScenePatch([new ScreenOp(source, removal), new ScreenOp(target, addition)]);

            _connections.Dispatch(patch);
            _events.Publish(new SessionEvent.ScenePatched(patch));

            HubLog.ItemMoved(_logger, source.Screen.Value, target.Screen.Value);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ItemId?> CopyItemAsync(
        ScreenRef source,
        ScreenRef target,
        ItemId item,
        Point? position,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var lying = _scenes.Get(source);

            if (lying.Items.FirstOrDefault(candidate => candidate.ItemId == item) is not { } template)
            {
                return null;
            }

            var context = _screens.ContextFor(target);
            var scene = _scenes.Get(target);
            var revision = _scenes.NextRevision();

            var copy = Arriving(template, scene, context, position, revision) with
            {
                ItemId = new ItemId(Guid.NewGuid()),

                // The copy is a picture that is wanted now, so it never lands in the fan.
                Parked = false,
                ParkedAt = 0,
            };

            // Where it goes, once it is no longer parked. Three cases, and only the first is the
            // ordinary one: an aimed drop point wins; a copy on the screen it came from steps
            // beside its template; and a copy of a PARKED template has no place of its own to step
            // beside, so it is placed like a new picture.
            if (position is null)
            {
                var centre = template.Parked
                ? Placement.NextPosition(scene, copy.Scale, copy.AspectRatio, context)
                : Placement.Beside(copy.CenterX, copy.CenterY, copy.Scale, copy.AspectRatio, context);

                copy = copy with { CenterX = centre.X, CenterY = centre.Y };
            }

            var addition = new AddItem(copy);

            _scenes.Set(target, SceneReducer.Apply(scene, addition, context));

            var patch = new ScenePatch([new ScreenOp(target, addition)]);

            _connections.Dispatch(patch);
            _events.Publish(new SessionEvent.ScenePatched(patch));

            HubLog.ItemCopied(_logger, target.Screen.Value);

            return copy.ItemId;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// What an item looks like when it lands on a screen: its own place unless one was aimed at,
    /// its size capped against the target's width, the top of the target's stack, and - if it is
    /// parked - the end of the target's fan. The fan itself is laid out by the reducer.
    /// </summary>
    private static SceneItem Arriving(
        SceneItem current, SceneState scene, ScreenContext context, Point? position, long revision)
    {
        var centre = position ?? new Point(current.CenterX, current.CenterY);

        return current with
        {
            CenterX = centre.X,
            CenterY = centre.Y,
            Scale = Math.Min(current.Scale, Layout.WidthCap(current.AspectRatio, context)),

            // Arriving counts as being touched (Part 3), and the number space is the target's.
            ZOrder = scene.TopZOrder + 1,
            Revision = revision,
            ParkedAt = current.Parked ? revision : 0,
        };
    }

    /// <inheritdoc />
    public async Task TransformItemAsync(
        ScreenRef screen,
        ItemTransform transform,
        bool fromTable,
        bool toFront,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transform);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        PatchOp op;

        try
        {
            var context = _screens.ContextFor(screen);
            var scene = _scenes.Get(screen);

            if (scene.Items.FirstOrDefault(item => item.ItemId == transform.Item) is not { } current)
            {
                // Gone in the meantime. With two ways of steering and hands on the table this is a
                // normal course of events, not an error (Part 11).
                return;
            }

            if (fromTable && current.Locked)
            {
                HubLog.LockedItemNotMoved(_logger, screen.Screen.Value);

                return;
            }

            // Everything the sender may not decide, in the order the arithmetic needs: the scale
            // first, because the hull the edge clamp measures depends on it.
            var scale = Layout.ClampScale(transform.Scale, current.AspectRatio, context);

            var held = Manipulation.HoldAtEdge(
                current with
                {
                    CenterX = transform.CenterX,
                    CenterY = transform.CenterY,
                    Scale = scale,
                    RotationDeg = transform.RotationDeg,
                },
                context);

            op = new TransformItem(
                transform.Item,
                held.CenterX,
                held.CenterY,
                held.Scale,
                held.RotationDeg,

                // What is taken hold of comes to the front (Part 3). Already on top counts as
                // done: raising it every twentieth of a second through a gesture would run the
                // number space up and change nothing anybody can see.
                //
                // A LOCKED item never rises, and the lock is asked HERE rather than being left to
                // the refusal above. That refusal only ever fires for the table, and until M4 the
                // table was the only caller that grabbed - so the promise "a locked item does not
                // change its ZOrder" (Part 11) held by accident. The thumbnail grabs with
                // fromTable: false, which is precisely the combination that had no test and would
                // have raised the item (Guide C15).
                ZOrder: toFront && !current.Locked
                    ? Math.Max(current.ZOrder, scene.TopZOrder + 1)
                    : current.ZOrder,
                Revision: _scenes.NextRevision());

            _scenes.Set(screen, SceneReducer.Apply(scene, op, context));

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
    public async Task RefitAsync(ScreenRef screen, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        List<PatchOp> ops = [];

        try
        {
            var context = _screens.ContextFor(screen);
            var scene = _scenes.Get(screen);

            if (scene.Items.Count == 0)
            {
                return;
            }

            // Parked pictures move because their bar does - it is measured in the new screen's
            // units - so the whole scene is fitted first and then read off. Working item by item
            // would leave the bar computed against a scene half of whose items had already moved.
            //
            // What does NOT follow is MinScale, and that is a property of the model rather than an
            // omission here: MinVisiblePixels is in DIP and means the same length on any screen,
            // while MinScale is a FRACTION of the screen height - the number that meant 80 DIP on
            // a 1080p table means 44 on an 800x600 one. Re-deriving it would need a screen to be
            // able to say "I have no opinion of my own", and there is no such state; it is the
            // same gap Part 6 records for the base size (M5b/M8).
            var fitted = Parking.Arrange(
                scene with
                {
                    Items =
                    [
                        .. scene.Items.Select(item => Manipulation.HoldAtEdge(
                            item with { Scale = Layout.ClampScale(item.Scale, item.AspectRatio, context) },
                            context)),
                    ],
                },
                context);

            foreach (var (before, after) in scene.Items.Zip(fitted.Items))
            {
                if (before == after)
                {
                    continue;
                }

                // The finished values travel, as everywhere else: the display is not asked to work
                // out what a changed screen means, or the two ends would each have their own idea
                // of where a picture ended up (Part 1, rule 2).
                ops.Add(new TransformItem(
                    after.ItemId,
                    after.CenterX,
                    after.CenterY,
                    after.Scale,
                    after.RotationDeg,
                    after.ZOrder,
                    _scenes.NextRevision()));
            }
        }
        finally
        {
            _gate.Release();
        }

        await ApplyAsync(screen, ops, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SetLockedAsync(
        ScreenRef screen, ItemId item, bool locked, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new SetLocked(item, locked), cancellationToken);

    /// <inheritdoc />
    public async Task UnlockAllAsync(ScreenRef screen, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PatchOp> ops;

        try
        {
            // Read under the same lock the write takes, then let go of it: which items are locked
            // has to be decided from the scene the patch is built against.
            ops =
            [
                .. _scenes.Get(screen).Items
                    .Where(item => item.Locked)
                    .Select(item => new SetLocked(item.ItemId, false)),
            ];
        }
        finally
        {
            _gate.Release();
        }

        await ApplyAsync(screen, ops, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ParkItemAsync(
        ScreenRef screen, ItemId item, bool parked, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        PatchOp op;

        try
        {
            var scene = _scenes.Get(screen);

            if (scene.Items.FirstOrDefault(candidate => candidate.ItemId == item) is not { } current)
            {
                return;
            }

            var revision = _scenes.NextRevision();

            op = new ParkItem(
                item,
                parked,

                // Coming back out of the fan counts as being touched, so it goes to the front like
                // anything else that is touched. Going in needs no depth of its own: the fan is
                // drawn ABOVE the whole table (Parking.FanAbove), because the one thing the players
                // must always be able to reach is the way to get a picture back.
                ZOrder: parked ? current.ZOrder : Math.Max(current.ZOrder, scene.TopZOrder + 1),
                Revision: revision,

                // The fan's own order. The same number the revision got, because it is the one
                // monotonic counter the hub already keeps - but in a field of its own, so that a
                // later change to a parked item cannot silently reshuffle the fan.
                ParkedAt: parked ? revision : 0);
        }
        finally
        {
            _gate.Release();
        }

        await ApplyAsync(screen, op, cancellationToken).ConfigureAwait(false);
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
    public bool TouchPoints { get; private set; } = true;

    /// <inheritdoc />
    public Task SetTouchPointsAsync(bool reporting, CancellationToken cancellationToken = default)
    {
        TouchPoints = reporting;

        // Every connected device at once. One that is switched off is not queued for: it is told
        // when it connects, out of this same value (HubEndpoints), because there is no per-device
        // wish here that a catalogue could keep.
        var update = new ConfigUpdateMessage(new ConfigUpdate([], TouchPoints: reporting));

        foreach (var connection in _connections.All)
        {
            _ = connection.TrySend(update);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SpotlightAsync(ScreenRef screen, Point at, CancellationToken cancellationToken = default)
    {
        // No gate and no catalogue, for the reason IdentifyScreens gives: there is nothing here to
        // read back. The ring is worth something now or not at all, so a device that is switched
        // off is not asked and nothing is kept for it.
        if (_connections.TryGet(screen.Device, out var connection))
        {
            _ = connection.TrySend(new SpotlightPulseMessage(screen.Screen, at.X, at.Y));

            HubLog.Spotlight(_logger, screen.Screen.Value);
        }

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
