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
            RemoveItem remove => Parking.Arrange(
                scene with { Items = [.. scene.Items.Where(item => item.ItemId != remove.Item)] },
                screen),
            SetBackground background => scene with { Background = background.Background },
            ClearBackground => scene with { Background = null },
            SetName name => ApplySetName(scene, name),
            SetShowName show => ApplyToOne(
                scene,
                show.Item,
                item => item is ImageItem image ? image with { ShowName = show.Show } : item,
                background => background with { ShowName = show.Show }),
            SetAnimationPaused paused => ApplyToOne(
                scene,
                paused.Item,
                item => item is ImageItem image ? image with { AnimationPaused = paused.Paused } : item,
                background => background with { AnimationPaused = paused.Paused }),
            ToggleItems items => scene with { ItemsVisible = items.Visible },
            ToggleBackground background => scene with { BackgroundVisible = background.Visible },
            TransformItem transform => ApplyTransform(scene, transform),
            SetLocked locked => ApplyToItem(
                scene, locked.Item, item => item with { Locked = locked.Locked }),
            ParkItem park => Parking.Arrange(
                ApplyToItem(
                    scene,
                    park.Item,
                    item => item with
                    {
                        Parked = park.Parked,
                        ZOrder = park.ZOrder,
                        Revision = park.Revision,

                        // Size and angle are NOT set here. Parking.Arrange owns them and applies
                        // them on every pass, which is what lets a scene move to a screen of a
                        // different shape and arrive as a fan rather than a heap (Part 6, rebuilt
                        // at the end of M3).
                        ParkedAt = park.Parked ? park.ParkedAt : 0,
                    }),
                screen),
            _ => scene,
        };
    }

    /// <summary>
    /// Moves an item to where it now lies. Nothing is computed - the hub has clamped the position,
    /// held the scale between its bounds and handed out the revision, and the display applies the
    /// very same values (Part 1, rule 2).
    /// <para>
    /// <b>An older revision is not refused here.</b> Ordering is the hub's, and by the time an
    /// operation reaches a reducer it has already won; a display that receives one out of order
    /// weighs it against its OWN running gesture, which is a question about the gesture and not
    /// about the scene (Part 4, conflict resolution).
    /// </para>
    /// </summary>
    private static SceneState ApplyTransform(SceneState scene, TransformItem op) =>
        ApplyToItem(
            scene,
            op.Item,
            item => item with
            {
                CenterX = op.CenterX,
                CenterY = op.CenterY,
                Scale = op.Scale,
                RotationDeg = op.RotationDeg,
                ZOrder = op.ZOrder,
                Revision = op.Revision,
            });

    /// <summary>
    /// One item by its id, or nothing at all. Unlike <see cref="ApplyToOne"/> there is no fallback
    /// to the background: a background layer cannot be pushed, locked or parked.
    /// </summary>
    private static SceneState ApplyToItem(SceneState scene, ItemId id, Func<SceneItem, SceneItem> change) =>
        scene with
        {
            Items = [.. scene.Items.Select(item => item.ItemId == id ? change(item) : item)],
        };

    /// <summary>
    /// Renames the ASSET wherever it shows on this screen - every item carrying it and the
    /// background too. An <see cref="AssetId"/> that does not appear here leaves the scene alone;
    /// that is not leniency but the normal case, because the control sends one of these per
    /// affected screen and a screen may simply not show the picture (Part 4).
    /// <para>
    /// <b>The revision is untouched</b>, here and in the three operations beside it.
    /// <c>Revision</c> orders TRANSFORMS - it is what a display weighs its own running gesture
    /// against (Part 4, conflict resolution) - and a caption has nothing to weigh against a
    /// gesture. Bumping it would make a rename look like a movement to the reconciliation in M3.
    /// </para>
    /// </summary>
    private static SceneState ApplySetName(SceneState scene, SetName op)
    {
        var background = scene.Background is { } current && current.AssetId == op.Asset
            ? current with { Name = op.Name }
            : scene.Background;

        return scene with
        {
            Background = background,
            Items =
            [
                .. scene.Items.Select(item =>
                    item is ImageItem image && image.AssetId == op.Asset
                        ? image with { Name = op.Name }
                        : item),
            ],
        };
    }

    /// <summary>
    /// The shape the caption and the animation switches share: one item, or the background when no
    /// item is named. Neither being there is no error - an operation for something this screen does
    /// not carry does nothing at all (Part 11).
    /// </summary>
    private static SceneState ApplyToOne(
        SceneState scene,
        ItemId? target,
        Func<SceneItem, SceneItem> toItem,
        Func<BackgroundItem, BackgroundItem> toBackground)
    {
        if (target is not { } id)
        {
            return scene.Background is { } background
                ? scene with { Background = toBackground(background) }
                : scene;
        }

        return scene with
        {
            Items = [.. scene.Items.Select(item => item.ItemId == id ? toItem(item) : item)],
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
