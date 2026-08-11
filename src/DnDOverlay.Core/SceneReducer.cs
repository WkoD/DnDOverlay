namespace DnDOverlay.Core;

/// <summary>
/// The one scene reducer. Every change to a scene goes through here - the hub applies it to the
/// authoritative state, the display applies the very same function to what arrives, and that
/// identity is the reason the two cannot drift apart (Part 1, rule 2).
/// <para>
/// It is pure: state in, state out, no clock, no configuration, no I/O. Everything a
/// computation needs is handed in as a <see cref="ScreenContext"/>, which is what makes the
/// display parameters take effect without the reducer ever reaching for a settings file
/// (Part 3).
/// </para>
/// </summary>
public static class SceneReducer
{
    /// <summary>
    /// Applies one operation to one screen's scene.
    /// <para>
    /// An operation this build does not know returns the scene unchanged rather than throwing.
    /// That is rule 7 as a property instead of a promise: an older display facing a newer
    /// control is simply one that does not know a few messages. The CALLER logs it - Core has no
    /// logger, and giving it one would be the first crack in "Core knows nobody".
    /// </para>
    /// </summary>
    public static SceneState Apply(SceneState scene, PatchOp op, ScreenContext screen)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(screen);

        return op switch
        {
            AddItem add => ApplyAddItem(scene, add),
            _ => scene,
        };
    }

    /// <summary>
    /// Puts a finished item into the scene. Placement, capping and <c>ZOrder</c> have already
    /// happened in the hub, so nothing is computed here - what arrives is the item.
    /// <para>
    /// An item whose <see cref="ItemId"/> is already present REPLACES it instead of appearing
    /// twice. Normally that does not occur; when it does - a patch delivered a second time after
    /// a reconnect - replacing is the outcome that leaves the scene consistent, and it costs
    /// nothing to be idempotent here.
    /// </para>
    /// </summary>
    private static SceneState ApplyAddItem(SceneState scene, AddItem add)
    {
        var existing = scene.Items.ToList();
        var index = existing.FindIndex(item => item.ItemId == add.Item.ItemId);

        if (index >= 0)
        {
            existing[index] = add.Item;
        }
        else
        {
            existing.Add(add.Item);
        }

        return scene with { Items = existing };
    }
}
