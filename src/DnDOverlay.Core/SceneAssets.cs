namespace DnDOverlay.Core;

/// <summary>
/// Which pictures a set of scenes is standing on. It is the one question three different stores ask
/// - the decoded bitmaps, the bytes kept for animations, and the picture store on disk - and they
/// have to agree, or one of them frees something another still draws.
/// <para>
/// <b>Why this exists at all:</b> a table keyed by <see cref="AssetId"/> that only ever grows is
/// the obvious convenience and the thing Part 6 rules out by name — "the sum only works if bitmaps
/// disappear again". Measured at the table (hand-run of M2b, step 44a): the display went to
/// <b>2 GB</b> and was still climbing, because nothing ever removed an entry.
/// </para>
/// </summary>
public static class SceneAssets
{
    /// <summary>
    /// Every picture these scenes reference, items and background alike.
    /// <para>
    /// Computed over ALL scenes the device holds, including the ones on screens that are not being
    /// drawn: an inactive screen keeps its arrangement (Part 3), so what lies there is still needed.
    /// </para>
    /// </summary>
    public static HashSet<AssetId> InUse(IEnumerable<SceneState> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        var wanted = new HashSet<AssetId>();

        foreach (var scene in scenes)
        {
            if (scene.Background is { } background)
            {
                wanted.Add(background.AssetId);
            }

            foreach (var item in scene.Items.OfType<ImageItem>())
            {
                wanted.Add(item.AssetId);
            }
        }

        return wanted;
    }
}
