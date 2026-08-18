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

    private long _revision;

    /// <summary>
    /// The scene of a screen, or an empty one. A screen the hub has never heard of is not an
    /// error - it is a screen nobody has played on yet.
    /// </summary>
    public SceneState Get(ScreenRef screen) =>
        _scenes.TryGetValue(screen, out var scene) ? scene : SceneState.Empty;

    public void Set(ScreenRef screen, SceneState scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        _scenes[screen] = scene;

        // Never below what is already lying on a table. A control that has just restarted takes
        // scenes over from the displays, and those items carry the numbers of the run before it -
        // handing out 1 again would have every display measuring the hub's new state against a
        // higher number of its own and keeping its own (Part 4, conflict resolution). It cost this
        // to notice: a gesture went up the wire, came back as a patch, and the number on it was
        // lower than the one the item already had.
        foreach (var item in scene.Items)
        {
            Raise(item.Revision);
        }
    }

    /// <summary>
    /// The next revision, and there is exactly one counter for the whole session. It lives here
    /// rather than in <see cref="SessionApi"/> because it belongs to what it numbers: whoever puts
    /// a scene in, from wherever, has to lift it.
    /// </summary>
    public long NextRevision() => Interlocked.Increment(ref _revision);

    private void Raise(long revision)
    {
        var current = Volatile.Read(ref _revision);

        while (revision > current)
        {
            var seen = Interlocked.CompareExchange(ref _revision, revision, current);

            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }

    /// <summary>
    /// Whether this screen has a scene of OUR making. It is what bounds the one exception to
    /// "the hub is authoritative": a control that has just restarted takes a device's scene over,
    /// but only where it has none itself - where it has one, it puts that through instead
    /// (Part 4).
    /// </summary>
    public bool Has(ScreenRef screen) => _scenes.ContainsKey(screen);

    /// <summary>Every screen that carries a scene, for the snapshots a connecting display gets.</summary>
    public IReadOnlyCollection<ScreenRef> Screens => _scenes.Keys.ToList();
}
