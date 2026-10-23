using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// What the scenes this device holds say about one picture: what the DM calls it, how wide its source
/// is, and whether it moves. Asked of the scenes at the moment of asking rather than kept in a table
/// beside the pictures - a second table over the same keys is the shape that cost 2 GB in M2b, and these
/// are asked a handful of times per picture.
/// </summary>
internal static class PictureFacts
{
    /// <summary>
    /// What the DM calls this picture - the first item or background showing it - and the start of its
    /// hash when nothing does any more: a picture can be dropped from a scene while its bytes are still on
    /// their way. Only this end knows that a hash is "Dilwyn Kemri", and a name is the whole point of a
    /// line about it (Part 8).
    /// </summary>
    internal static string NameOf(IEnumerable<SceneState> scenes, AssetId asset)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        foreach (var scene in scenes)
        {
            foreach (var item in scene.Items)
            {
                if (item is ImageItem picture && picture.AssetId == asset)
                {
                    return picture.Name;
                }
            }

            if (scene.Background is { } background && background.AssetId == asset)
            {
                return background.Name ?? asset.Value[..8];
            }
        }

        return asset.Value[..8];
    }

    /// <summary>
    /// How many pixels the source has across, from the scenes that carry the picture. Zero when nothing
    /// does any more - a picture dropped while its sharpening was on its way, which then asks for no step.
    /// </summary>
    internal static int SourceWidth(IEnumerable<SceneState> scenes, AssetId asset)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        foreach (var scene in scenes)
        {
            foreach (var item in scene.Items.OfType<ImageItem>())
            {
                if (item.AssetId == asset)
                {
                    return item.Meta.PixelWidth;
                }
            }

            if (scene.Background is { } background && background.AssetId == asset)
            {
                return background.Meta.PixelWidth;
            }
        }

        return 0;
    }

    /// <summary>
    /// Whether any scene says this picture moves - its bytes are then kept for the animation. Asked of
    /// the SCENE rather than of the bytes: the codec has already worked it out, and asking again would be
    /// a second answer to a settled question (Part 5).
    /// </summary>
    internal static bool Animated(IEnumerable<SceneState> scenes, AssetId asset)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        return scenes.Any(scene =>
            (scene.Background is { } background
                && background.AssetId == asset
                && background.Meta.IsAnimated)
            || scene.Items.OfType<ImageItem>().Any(item => item.AssetId == asset && item.Meta.IsAnimated));
    }
}
