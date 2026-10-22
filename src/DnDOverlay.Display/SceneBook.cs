using System.Collections.Concurrent;
using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// The scenes this device holds, one per screen.
/// <para>
/// <b>Written from two threads by construction</b>: the hub's patches and snapshots land on the
/// connection task, a hand letting go writes its gesture in on the UI thread at once (Part 4,
/// conflict rule 2). A read-modify-write on each side of a plain table loses one of the two - the
/// release that springs back, or a patch the DM sent that never shows. So every change goes through
/// one lock here, from the read to the write; reads stay without it, because the fetch, sharpen and
/// pulse threads walk the table while it changes and a concurrent table lets them.
/// </para>
/// </summary>
internal sealed class SceneBook
{
    private readonly ConcurrentDictionary<ScreenRef, SceneState> _scenes = new();
    private readonly Lock _writing = new();

    internal ICollection<ScreenRef> Keys => _scenes.Keys;

    internal ICollection<SceneState> Values => _scenes.Values;

    /// <summary>Every screen with its scene, as a snapshot - safe to walk while the book changes.</summary>
    internal KeyValuePair<ScreenRef, SceneState>[] Pairs => _scenes.ToArray();

    internal bool TryGetValue(ScreenRef screen, out SceneState scene) =>
        _scenes.TryGetValue(screen, out scene!);

    internal bool ContainsKey(ScreenRef screen) => _scenes.ContainsKey(screen);

    /// <summary>The scenes of one device - the ones a <c>Hello</c> carries and the memory rule counts.</summary>
    internal IEnumerable<KeyValuePair<ScreenRef, SceneState>> Of(DeviceId device) =>
        Pairs.Where(pair => pair.Key.Device == device);

    /// <summary>Puts a whole scene down - a snapshot, which replaces whatever stood there.</summary>
    internal void Set(ScreenRef screen, SceneState scene)
    {
        lock (_writing)
        {
            _scenes[screen] = scene;
        }
    }

    /// <summary>
    /// Changes one scene from what it is right now, with no other change in between. The change is
    /// handed the scene or <see langword="null"/> when there is none, and returns the new one, or
    /// <see langword="null"/> to leave things as they are - a gesture for a screen without a scene
    /// must not invent an empty one, which would count as "this screen has its scene".
    /// </summary>
    internal void Change(ScreenRef screen, Func<SceneState?, SceneState?> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_writing)
        {
            var now = _scenes.TryGetValue(screen, out var known) ? known : null;

            if (change(now) is { } changed)
            {
                _scenes[screen] = changed;
            }
        }
    }

    internal void Clear()
    {
        lock (_writing)
        {
            _scenes.Clear();
        }
    }

    /// <summary>
    /// Moves every scene of one device to another identity - the device took a fresh one, and what
    /// it holds is still its own (Part 3, Part 7).
    /// </summary>
    internal void Rekey(DeviceId from, DeviceId to)
    {
        lock (_writing)
        {
            foreach (var (screen, scene) in _scenes.ToArray())
            {
                if (screen.Device == from && _scenes.TryRemove(screen, out _))
                {
                    _scenes[screen with { Device = to }] = scene;
                }
            }
        }
    }
}
