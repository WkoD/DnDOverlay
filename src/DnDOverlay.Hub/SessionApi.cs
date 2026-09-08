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

            var item = Arriving(scene, context, asset, position);

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
    public async Task<IReadOnlyList<ItemId>> AddItemsAsync(
        ScreenRef screen,
        IReadOnlyList<AssetRef> assets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (assets.Count == 0)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var context = _screens.ContextFor(screen);
            var scene = _scenes.Get(screen);

            var ops = new List<ScreenOp>(assets.Count);
            var arrived = new List<ItemId>(assets.Count);

            foreach (var asset in assets)
            {
                var op = new AddItem(Arriving(scene, context, asset, position: null));

                ops.Add(new ScreenOp(screen, op));
                arrived.Add(op.Item.ItemId);

                // Folded before the next one is worked out - see Arriving for what happens without
                // this line.
                scene = SceneReducer.Apply(scene, op, context);
            }

            _scenes.Set(screen, scene);

            // ONE patch, and that is the whole point of this method. Measured at the table
            // (07.09.2026): as one AddItem per picture, a run of 714 filled a subscriber queue that
            // holds 256, the hub ended that stream as it must, and the control went deaf. The wire,
            // the display and the reducer were built for a patch of many ops all along - the
            // display even groups by screen so that a load of twenty is one drawing rather than
            // twenty. Only the control never sent one.
            var patch = new ScenePatch(ops);

            _connections.Dispatch(patch);
            _events.Publish(new SessionEvent.ScenePatched(patch));

            return arrived;
        }
        finally
        {
            _gate.Release();
        }
    }


    /// <summary>
    /// The finished item one arriving picture becomes on this screen, worked out against the scene
    /// it is arriving INTO.
    /// <para>
    /// Pulled out of <c>AddItemAsync</c> so that a run of arrivals can fold: the place comes from
    /// <see cref="Placement"/>, which counts what already lies there, and the depth from
    /// <c>TopZOrder</c>. Both read the scene, so a batch that worked every item out against the
    /// SAME starting scene would put all of them on one grid place at one depth - seven hundred
    /// pictures exactly on top of each other, looking for all the world like one.
    /// </para>
    /// </summary>
    private ImageItem Arriving(SceneState scene, ScreenContext context, AssetRef asset, Point? position)
    {
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

        return item;
    }

    /// <summary>
    /// What parking or unparking one item comes to, worked out against the scene it happens in.
    /// <para>
    /// Pulled out of <c>ParkItemAsync</c> so a whole selection can be parked in one patch and each
    /// card still gets its own depth and its own place in the fan's order - worked out against the
    /// scene AS IT FILLS, not against the one the run started from.
    /// </para>
    /// </summary>
    private ParkItem Parked(SceneState scene, SceneItem current, bool parked)
    {
        var revision = _scenes.NextRevision();

        return new ParkItem(
            current.ItemId,
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

    /// <inheritdoc />
    public Task RemoveItemAsync(ScreenRef screen, ItemId item, CancellationToken cancellationToken = default) =>
        ApplyAsync(screen, new RemoveItem(item), cancellationToken);

    /// <inheritdoc />
    public Task RemoveItemsAsync(
        ScreenRef screen, IReadOnlyList<ItemId> items, CancellationToken cancellationToken = default) =>
        ManyAsync(
            screen, items, (_, current) => new RemoveItem(current.ItemId), inOrder: null, cancellationToken);

    /// <inheritdoc />
    public Task ParkItemsAsync(
        ScreenRef screen, IReadOnlyList<ItemId> items, bool parked, CancellationToken cancellationToken = default) =>
        ManyAsync(
            screen,
            items,
            (scene, current) => Parked(scene, current, parked),

            // <b>Out of the fan in the fan's own order, not in the order the cards were picked.</b>
            // Unparking hands out fresh depths as it goes, so whatever order this run has IS the
            // order the cards end up lying in on the table. A selection arrives here in whatever
            // order the DM tapped or the frame caught (Selection is oldest-choice-first by design,
            // because the focus of M5b reads exactly that) - and none of those is the order the
            // player saw in the fan. Going IN needs no key: the fan is drawn above the table and
            // ParkedAt is handed out as the run proceeds.
            parked ? null : item => item.ParkedAt,
            cancellationToken);

    /// <inheritdoc />
    public Task SetItemsLockedAsync(
        ScreenRef screen, IReadOnlyList<ItemId> items, bool locked, CancellationToken cancellationToken = default) =>
        ManyAsync(
            screen, items, (_, current) => new SetLocked(current.ItemId, locked), inOrder: null, cancellationToken);

    /// <inheritdoc />
    public Task SetItemsShowNameAsync(
        ScreenRef screen, IReadOnlyList<ItemId> items, bool show, CancellationToken cancellationToken = default) =>
        ManyAsync(
            screen, items, (_, current) => new SetShowName(current.ItemId, show), inOrder: null, cancellationToken);

    /// <inheritdoc />
    public Task SetItemsAnimationPausedAsync(
        ScreenRef screen, IReadOnlyList<ItemId> items, bool paused, CancellationToken cancellationToken = default) =>
        ManyAsync(
            screen,
            items,
            (_, current) => new SetAnimationPaused(current.ItemId, paused),
            inOrder: null,
            cancellationToken);

    /// <summary>
    /// One command over a selection: the operations are worked out item by item against the scene
    /// <b>as it changes</b>, and then go out as a single patch.
    /// <para>
    /// The folding is not caution. Parking derives a depth and a place in the fan's order from the
    /// scene, so a run built against one starting scene would hand every card the same two numbers
    /// - a fan in which nothing has an order.
    /// </para>
    /// <para>
    /// An item that is no longer there is skipped rather than refused, exactly as the single forms
    /// do it: with two ways of steering and hands on the table, gone-in-the-meantime is a normal
    /// course of events and not an error (Part 11).
    /// </para>
    /// <para>
    /// The gate is let go before <c>ApplyAsync</c> takes it again, which is the shape every single
    /// form here already has - the operations are built against a scene that could in principle
    /// have moved on by the time they are applied. Keeping the gate across both would be a second
    /// locking discipline in one class, which is worse than the window it closes.
    /// </para>
    /// <para>
    /// <b><paramref name="inOrder"/> is how a command says which order it means</b>, and it is
    /// asked HERE rather than of the caller because the key is read off the scene, which only the
    /// hub has. A selection arrives in the order it was picked; for some commands that is the right
    /// one and for others it is not. Sorting in the hub means no caller can get it wrong, where
    /// sorting at the call site means every caller can.
    /// </para>
    /// </summary>
    private async Task ManyAsync(
        ScreenRef screen,
        IReadOnlyList<ItemId> items,
        Func<SceneState, SceneItem, PatchOp> op,
        Func<SceneItem, long>? inOrder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        List<PatchOp> ops = [];

        try
        {
            var context = _screens.ContextFor(screen);
            var scene = _scenes.Get(screen);

            // Read off the scene the run STARTS from, before anything has been folded into it.
            var order = inOrder is null ? items : Sorted(scene, items, inOrder);

            foreach (var id in order)
            {
                if (scene.Items.FirstOrDefault(candidate => candidate.ItemId == id) is not { } current)
                {
                    continue;
                }

                var built = op(scene, current);

                ops.Add(built);
                scene = SceneReducer.Apply(scene, built, context);
            }
        }
        finally
        {
            _gate.Release();
        }

        await ApplyAsync(screen, ops, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A selection put into the order one command means, by a key read off the scene it is standing
    /// in. What is no longer there falls out; the caller's loop skips it in any case.
    /// </summary>
    private static IReadOnlyList<ItemId> Sorted(
        SceneState scene, IReadOnlyList<ItemId> items, Func<SceneItem, long> key)
    {
        var picked = items.ToHashSet();

        // Walked over the SCENE and not over the selection, so that OrderBy - which is stable - has
        // a defined starting order even where the key ties.
        return
        [
            .. scene.Items
                .Where(item => picked.Contains(item.ItemId))
                .OrderBy(key)
                .Select(item => item.ItemId),
        ];
    }

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
    /// <inheritdoc />
    public async Task TransformBackgroundAsync(
        ScreenRef screen,
        Point centre,
        double scale,
        double rotationDeg,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        BackgroundItem wanted;

        try
        {
            if (_scenes.Get(screen).Background is not { } background)
            {
                return;
            }

            var context = _screens.ContextFor(screen);

            wanted = Manipulation.HoldAtEdge(
                background with
                {
                    CenterX = centre.X,
                    CenterY = centre.Y,
                    Scale = Layout.ClampScale(scale, background.Meta.AspectRatio, context),
                    RotationDeg = rotationDeg,
                },
                context);

            // <b>The tick moves only when something actually moved.</b> Switching the hand mode on
            // and putting a finger down sends a step of nothing - the release reports whatever the
            // hold is holding - and the DM would lose the answer to "what did I set this to" by
            // merely opening the menu (07.09.2026). Compared with a tolerance rather than exactly,
            // because the clamp and the snap on release can return the same arrangement through a
            // last bit of arithmetic.
            wanted = Moved(background, wanted) ? wanted with { Fit = null } : wanted with { Fit = background.Fit };
        }
        finally
        {
            _gate.Release();
        }

        await ApplyAsync(screen, new SetBackground(wanted), cancellationToken).ConfigureAwait(false);
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

            // The angle goes back to nothing with them. Both buttons put the picture into one of
            // the two obvious positions, and a background left standing at 15 degrees was in
            // neither of them (hand-run of M4, 38b).
            wanted = background with
            {
                CenterX = centre.X,
                CenterY = centre.Y,
                Scale = scale,
                RotationDeg = 0,

                // Which of the two it now stands in, so the menu can say so.
                Fit = fit,
            };
        }
        finally
        {
            _gate.Release();
        }

        await ApplyAsync(screen, new SetBackground(wanted), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether these two arrangements are different ones. <b>A tolerance rather than an exact
    /// comparison</b>: a gesture that grips and lets go without travelling still travels through
    /// the edge clamp and the quarter-turn snap, and coming back the same to the last bit is not
    /// the question being asked. A tenth of a degree and a thousandth of a screen are below what a
    /// hand can do on purpose.
    /// </summary>
    private static bool Moved(BackgroundItem before, BackgroundItem after) =>
        Math.Abs(before.CenterX - after.CenterX) > 0.001
        || Math.Abs(before.CenterY - after.CenterY) > 0.001
        || Math.Abs(before.Scale - after.Scale) > 0.001
        || Math.Abs(before.RotationDeg - after.RotationDeg) > 0.1;

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
            // Out of the fan and onto the table, like a copy. Part 11 has it staying parked and
            // joining the target's fan, and the table said otherwise (hand-run of M4, 25b): a
            // picture dragged to another screen is one that is WANTED there, and it arrived where
            // nobody was looking. The plan and its test are corrected with this.
            var arriving = Arriving(current, scene, context, position, revision) with
            {
                Parked = false,
                ParkedAt = 0,
            };

            // <b>And it needs a place of its own.</b> A parked card's own coordinates are its slot
            // in the source's fan, hard against the park edge - so an item moved out of the fan
            // without an aimed drop point arrived unparked and yet lying exactly where a fan lies,
            // which at the table is indistinguishable from having been put into the target's fan.
            // That is what the second hand-run reported (25b), and the rule was already written
            // one method down: CopyItemAsync places a copy of a parked template "like a new
            // picture". Copy had it, move did not - the shape Guide C15 describes.
            if (position is null && current.Parked)
            {
                var centre = Placement.NextPosition(scene, arriving.Scale, arriving.AspectRatio, context);

                arriving = arriving with { CenterX = centre.X, CenterY = centre.Y };
            }

            var addition = new AddItem(arriving);

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

    /// <inheritdoc />
    public Task MoveItemsAsync(
        ScreenRef source,
        ScreenRef target,
        IReadOnlyList<ItemId> items,
        CancellationToken cancellationToken = default) =>
        RelocateAsync(source, target, items, copy: false, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ItemId>> CopyItemsAsync(
        ScreenRef source,
        ScreenRef target,
        IReadOnlyList<ItemId> items,
        CancellationToken cancellationToken = default) =>
        RelocateAsync(source, target, items, copy: true, cancellationToken);

    /// <summary>
    /// A whole selection sent to another screen, as one patch and in the order the DM can see.
    /// <para>
    /// <b>The order is the stack's, and it has to be read here.</b> A selection carries the order
    /// it was PICKED in - <c>Selection</c> is oldest-choice-first on purpose, because the focus of
    /// M5b reads exactly that - and the menu hands over whatever order the scene happens to hold.
    /// Neither is the order the DM sees on the table. Landing hands out rising depths as the run
    /// proceeds, so whatever order this loop has becomes the stacking order on the target: five
    /// pictures that lay one on top of the other have to be walked bottom-up, or they arrive
    /// shuffled. That is what <see cref="Sorted"/> is for.
    /// </para>
    /// <para>
    /// <b>New depths, not the old ones.</b> The target keeps its own number space, so carrying the
    /// source's depths across would interleave the arrivals with what is already lying there - in
    /// the bad case underneath it, which for a picture somebody deliberately sent over is the one
    /// outcome nobody wants. They arrive as a closed block on top, in their own order.
    /// </para>
    /// <para>
    /// Both scenes are folded, and for the same reason a run of arrivals is: <c>Arriving</c> reads
    /// the target's <c>TopZOrder</c> and <c>Placement</c> counts what already lies there. Copying
    /// onto the source screen is allowed and moving onto it is not - the same asymmetry the single
    /// forms have (Part 4) - so the source is folded only when something actually leaves it.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<ItemId>> RelocateAsync(
        ScreenRef source,
        ScreenRef target,
        IReadOnlyList<ItemId> items,
        bool copy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0 || (!copy && source == target))
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var leaving = _screens.ContextFor(source);
            var context = _screens.ContextFor(target);
            var lying = _scenes.Get(source);
            var scene = _scenes.Get(target);

            // Bottom of the stack first, so that the depths handed out below rebuild the same
            // stack on the other side.
            var order = Sorted(lying, items, item => item.ZOrder);

            var ops = new List<ScreenOp>(copy ? order.Count : order.Count * 2);
            var landed = new List<ItemId>(order.Count);

            foreach (var id in order)
            {
                // Read from the SOURCE as it started: a copy onto the screen it came from must not
                // find its own copies and go on copying those.
                if (lying.Items.FirstOrDefault(candidate => candidate.ItemId == id) is not { } current)
                {
                    continue;
                }

                var revision = _scenes.NextRevision();

                var arriving = Arriving(current, scene, context, position: null, revision) with
                {
                    // Sent over is wanted over there, so it never lands in the fan - the same
                    // correction the single forms carry (hand-run of M4, 25b).
                    Parked = false,
                    ParkedAt = 0,
                };

                if (copy)
                {
                    arriving = arriving with { ItemId = new ItemId(Guid.NewGuid()) };
                }

                // The three cases of the single forms, unchanged: a parked picture has no place of
                // its own - its coordinates are its slot in the fan - so it is placed like a new
                // one; a copy steps beside its template; a moved picture keeps where it lay.
                if (current.Parked)
                {
                    var centre = Placement.NextPosition(scene, arriving.Scale, arriving.AspectRatio, context);

                    arriving = arriving with { CenterX = centre.X, CenterY = centre.Y };
                }
                else if (copy)
                {
                    var centre = Placement.Beside(
                        arriving.CenterX, arriving.CenterY, arriving.Scale, arriving.AspectRatio, context);

                    arriving = arriving with { CenterX = centre.X, CenterY = centre.Y };
                }

                if (!copy)
                {
                    var removal = new RemoveItem(id);

                    ops.Add(new ScreenOp(source, removal));
                    lying = SceneReducer.Apply(lying, removal, leaving);
                }

                var addition = new AddItem(arriving);

                ops.Add(new ScreenOp(target, addition));
                scene = SceneReducer.Apply(scene, addition, context);
                landed.Add(arriving.ItemId);
            }

            if (ops.Count == 0)
            {
                // Every one of them had gone in the meantime. A patch with no operations would
                // still be a step in the timeline.
                return [];
            }

            if (!copy)
            {
                _scenes.Set(source, lying);
            }

            _scenes.Set(target, scene);

            // ONE patch over two screens, exactly as the single forms make one over two. Both
            // halves reach every display, and the arrival highlight reads the ops of ITS screen -
            // plain AddItems on the target, plain RemoveItems on the source (Arrival).
            var patch = new ScenePatch(ops);

            _connections.Dispatch(patch);
            _events.Publish(new SessionEvent.ScenePatched(patch));

            if (copy)
            {
                HubLog.ItemsCopied(_logger, landed.Count, target.Screen.Value);
            }
            else
            {
                HubLog.ItemsMoved(_logger, landed.Count, source.Screen.Value, target.Screen.Value);
            }

            return landed;
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

            op = Transformed(scene, context, current, transform, toFront);

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
    public Task TransformItemsAsync(
        ScreenRef screen,
        IReadOnlyList<ItemTransform> transforms,
        bool toFront,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transforms);

        var wanted = new Dictionary<ItemId, ItemTransform>();

        foreach (var transform in transforms)
        {
            wanted[transform.Item] = transform;
        }

        return ManyAsync(
            screen,
            [.. wanted.Keys],

            // ContextFor inside the lambda and not hoisted: this runs under the gate, where every
            // other read of the catalogue in this class happens.
            (scene, current) => Transformed(
                scene, _screens.ContextFor(screen), current, wanted[current.ItemId], toFront),

            // <b>No key, and that is the answer rather than an omission.</b> Without toFront no
            // depth is handed out, so the run has no order to get wrong; with it, the order the
            // caller passed IS what the command means - unlike a relocation, where the order that
            // matters is the one the DM sees and only the scene knows it.
            inOrder: null,
            cancellationToken);
    }

    /// <summary>
    /// What one transform comes to once the hub has had its say, worked out against the scene it
    /// happens in.
    /// <para>
    /// Pulled out of <c>TransformItemAsync</c> so a whole selection can be turned in one patch and
    /// each picture still be clamped against the screen on its own. With <paramref name="toFront"/>
    /// the depth comes from <c>TopZOrder</c>, so a run has to be folded between one and the next.
    /// </para>
    /// </summary>
    private TransformItem Transformed(
        SceneState scene, ScreenContext context, SceneItem current, ItemTransform transform, bool toFront)
    {
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

        return new TransformItem(
            transform.Item,
            held.CenterX,
            held.CenterY,
            held.Scale,
            held.RotationDeg,

            // What is taken hold of comes to the front (Part 3). Already on top counts as
            // done: raising it every twentieth of a second through a gesture would run the
            // number space up and change nothing anybody can see.
            //
            // What is taken hold of comes to the front, LOCKED OR NOT, and that is a
            // correction from the table (hand-run of M4, 20). M4a had read Part 3's "not raised
            // for locked items" as a rule and asked the lock here; the DM asked for the
            // opposite, and the reason is in the same sentence: Part 3 gives its rule the
            // reason "they cannot be taken hold of", which is true at the TABLE and false in
            // the thumbnail. A picture the DM has just touched has to be the one he sees.
            // At the table the question does not arise - a locked item never reaches here.
            ZOrder: toFront
                ? Math.Max(current.ZOrder, scene.TopZOrder + 1)
                : current.ZOrder,
            Revision: _scenes.NextRevision());
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

            op = Parked(scene, current, parked);
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
        var reached = _connections.TryGet(screen.Device, out var connection);

        if (reached)
        {
            _ = connection!.TrySend(new SpotlightPulseMessage(screen.Screen, at.X, at.Y));
        }

        // Outside the branch, and on Information rather than Debug. Both came from the second
        // hand-run of M4: the runner reported "no spotlight at all", and neither log could say
        // whether the gesture had fired, whether it had been sent, or whether it had arrived and
        // not been drawn - the only line about it was Debug, which no log file carries. This is
        // the one grip whose whole result is something a person has to SEE, which is the same
        // argument the screen-name line makes for itself (3018). A silent drop now says so.
        HubLog.Spotlight(_logger, screen.Screen.Value, reached);

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
