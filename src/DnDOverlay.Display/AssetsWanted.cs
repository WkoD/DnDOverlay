using DnDOverlay.Core;
using DnDOverlay.Transport;

namespace DnDOverlay.Display;

/// <summary>
/// What a scene still needs from the hub: every picture not decoded yet - and every picture marked
/// for a refill, decoded or not.
/// <para>
/// <b>The refill is what "clear the picture store" needs</b> (Part 7): the store is emptied and filled
/// again for what lies on the screens. Asking only for what is not decoded fetched nothing - every
/// picture on the table is decoded - so the store stayed empty, and with it went sharpening past the
/// decode step (it reads the original from the store), stepping down under memory pressure (it reads
/// the thumbnail from there) and the stock a crash would restore from.
/// </para>
/// </summary>
internal static class AssetsWanted
{
    /// <param name="scene">The scene of one screen.</param>
    /// <param name="decoded">Whether a picture is decoded already.</param>
    /// <param name="takeRefill">
    /// Whether a picture is marked for a refill - and takes the mark, so one run fetches it once.
    /// </param>
    internal static List<AssetWanted> Of(SceneState scene, Func<AssetId, bool> decoded, Func<AssetId, bool> takeRefill)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(takeRefill);

        var wanted = new List<AssetWanted>();

        if (scene.Background is { } background && Needed(background.AssetId))
        {
            wanted.Add(new AssetWanted(background.AssetId, background.Meta, IsBackground: true));
        }

        wanted.AddRange(scene.Items
            .OfType<ImageItem>()
            .Where(item => Needed(item.AssetId))
            .Select(item => new AssetWanted(item.AssetId, item.Meta, IsParked: item.Parked)));

        return wanted;

        // The mark is taken first, so a picture that is fetched anyway does not keep it for a second run.
        bool Needed(AssetId asset)
        {
            var refill = takeRefill(asset);

            return refill || !decoded(asset);
        }
    }
}
