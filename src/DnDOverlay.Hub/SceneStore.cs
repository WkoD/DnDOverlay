using System.Collections.Concurrent;
using DnDOverlay.Core;

namespace DnDOverlay.Hub;

/// <summary>
/// The authoritative arrangement: one scene per <see cref="ScreenRef"/> - per SCREEN, not per
/// device, because a display PC with two monitors is the normal case (Part 3).
/// <para>
/// It is held in memory and written nowhere. That is not an omission: the arrangement is
/// deliberately transient, and it survives almost every failure because whichever side connects
/// hands it to the one that lost it (Part 1, idea 3; Part 4). The MATERIAL is a different thing
/// and lives on disk, owned by the campaign.
/// </para>
/// <para>
/// Access is serialised by <see cref="SessionApi"/>, which is the only writer. This type keeps a
/// concurrent dictionary anyway so that a reader - the endpoint sending a snapshot - never sees
/// a torn map.
/// </para>
/// </summary>
public sealed class SceneStore
{
    private readonly ConcurrentDictionary<ScreenRef, SceneState> _scenes = new();

    /// <summary>
    /// The scene of a screen, or an empty one. A screen the hub has never heard of is not an
    /// error - it is a screen nobody has played on yet.
    /// </summary>
    public SceneState Get(ScreenRef screen) =>
        _scenes.TryGetValue(screen, out var scene) ? scene : SceneState.Empty;

    public void Set(ScreenRef screen, SceneState scene) => _scenes[screen] = scene;

    /// <summary>Every screen that carries a scene, for the snapshots a connecting display gets.</summary>
    public IReadOnlyCollection<ScreenRef> Screens => _scenes.Keys.ToList();
}
