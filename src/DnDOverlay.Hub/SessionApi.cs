using DnDOverlay.Core;

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
    private readonly SemaphoreSlim _gate = new(1, 1);

    private long _revision;

    public SessionApi(SceneStore scenes, ScreenCatalog screens, DisplayConnections connections)
    {
        _scenes = scenes;
        _screens = screens;
        _connections = connections;
    }

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

    public void Dispose() => _gate.Dispose();
}
