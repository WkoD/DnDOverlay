using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// The decoded pictures the memory rule may step down to their thumbnails, as
/// <see cref="MemoryPressure"/> sees them (Part 6).
/// <para>
/// <b>Not one already down, not one that moves, not one any background shows, not one no picture on
/// a screen of this device uses.</b> A moving picture's frames are built from its bytes, so a
/// thumbnail would be a second picture for the same item; a blurred background is the whole table
/// gone soft. A picture counts as parked only when every item showing it lies in the fan, and its age
/// is its place there (the lowest depth was parked longest ago) - on the table it is when a hand last
/// touched it. A picture a hand is on is handed over as held, which the rule never steps down.
/// </para>
/// </summary>
internal static class ReliefCandidates
{
    /// <param name="scenes">The scenes of this device - every screen, the inactive ones included (Part 3).</param>
    /// <param name="decoded">Every decoded bitmap with its size in pixels.</param>
    /// <param name="steppedDown">Whether memory pressure has already put a picture down.</param>
    /// <param name="moving">Whether a picture is animated - its bytes are kept for its frames.</param>
    /// <param name="touchedAt">When a picture was last touched or arrived, as a running count.</param>
    /// <param name="held">The items a hand is on right now.</param>
    internal static List<DecodedPicture> Of(
        IEnumerable<SceneState> scenes,
        IEnumerable<(AssetId Asset, int PixelWidth, int PixelHeight)> decoded,
        Func<AssetId, bool> steppedDown,
        Func<AssetId, bool> moving,
        Func<AssetId, long> touchedAt,
        IReadOnlySet<ItemId> held)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(steppedDown);
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(touchedAt);
        ArgumentNullException.ThrowIfNull(held);

        var mine = scenes.ToList();
        var backgrounds = mine.Where(scene => scene.Background is not null).Select(scene => scene.Background!.AssetId).ToHashSet();
        var items = mine.SelectMany(scene => scene.Items.OfType<ImageItem>()).ToList();
        var pictures = new List<DecodedPicture>();

        foreach (var (asset, width, height) in decoded)
        {
            if (steppedDown(asset) || moving(asset) || backgrounds.Contains(asset))
            {
                continue;
            }

            var showing = items.Where(item => item.AssetId == asset).ToList();

            if (showing.Count == 0)
            {
                continue;
            }

            var parked = showing.All(item => item.Parked);

            pictures.Add(new DecodedPicture(
                asset,
                (long)width * height * 4,
                parked,
                parked ? showing.Min(item => item.ZOrder) : touchedAt(asset),
                showing.Any(item => held.Contains(item.ItemId))));
        }

        return pictures;
    }
}
