namespace DnDOverlay.Core;

/// <summary>
/// One decoded picture as the memory rule sees it.
/// </summary>
/// <param name="Asset">The picture. Bitmaps hang on the picture, not on the item: two items on one share one.</param>
/// <param name="Bytes">What its bitmap costs now - width × height × 4.</param>
/// <param name="Parked">Whether every item showing it lies in the fan.</param>
/// <param name="Age">
/// Smaller is older: for a parked picture its place in the fan (the lowest <c>ZOrder</c> was parked
/// longest ago), for one on the table when a hand last touched it.
/// </param>
/// <param name="Held">A hand is on it right now. Never stepped down - that would be a gesture going blurred under the finger.</param>
public readonly record struct DecodedPicture(AssetId Asset, long Bytes, bool Parked, long Age, bool Held);

/// <summary>
/// What gives way when the display runs out of memory (Part 6): first the pictures in the fan,
/// then the pictures on the table - each stepped down to its thumbnail, the longest unused first,
/// never the one a hand is on.
/// <para>
/// <b>Two thresholds, because they protect two different things.</b> At three quarters of the cap
/// the fan gives way: of a parked picture only a strip shows, and a full bitmap for it is waste on
/// a 4K table and harmless on a 1080p one, which is why it waits for pressure instead of always
/// happening - unparking is a player's gesture, and it should not come back blurred several times
/// an evening. At the cap itself the table gives way too, the picture untouched longest first
/// (decided by the DM, 19.10.2026): a new picture arrives sharp, and an old one nobody has looked at
/// in an hour goes soft. Nothing is refused and nothing disappears.
/// </para>
/// <para>
/// <b>The cap is a fraction of what the machine has, not a fixed number.</b> 1.5 GB of committed
/// memory, measured in spike B, but never more than a quarter of the physical memory: a mini PC with
/// 4 GB shares it with Windows, and paging during a gesture is the one stall rank 1 may not have.
/// There is no separate limit on pictures per screen any more - it was derived from this budget,
/// and with the budget enforced it would be the same rule twice (19.10.2026).
/// </para>
/// </summary>
public static class MemoryPressure
{
    /// <summary>The committed-memory ceiling of spike B.</summary>
    public const long Ceiling = 1536L * 1024 * 1024;

    /// <summary>The share of the cap at which the fan starts to give way.</summary>
    public const double FanShare = 0.75;

    /// <summary>The cap for a machine with this much physical memory.</summary>
    public static long Cap(long physicalBytes) =>
        physicalBytes <= 0 ? Ceiling : Math.Min(Ceiling, physicalBytes / 4);

    /// <summary>
    /// The pictures to step down to their thumbnails, in the order they should go, so that the
    /// process comes back under the line it crossed.
    /// </summary>
    /// <param name="usedBytes">Committed memory of the process, as measured.</param>
    /// <param name="cap">From <see cref="Cap"/>.</param>
    /// <param name="pictures">Every decoded picture that is still at more than its thumbnail.</param>
    /// <param name="thumbnailBytes">What a picture costs once stepped down - the saving is the difference.</param>
    public static IReadOnlyList<AssetId> StepDown(
        long usedBytes,
        long cap,
        IReadOnlyList<DecodedPicture> pictures,
        long thumbnailBytes)
    {
        ArgumentNullException.ThrowIfNull(pictures);

        var steps = new List<AssetId>();
        var projected = usedBytes;
        var fanLine = (long)(cap * FanShare);

        if (projected <= fanLine)
        {
            return steps;
        }

        long Saving(DecodedPicture picture) => Math.Max(0, picture.Bytes - thumbnailBytes);

        foreach (var picture in pictures.Where(one => one.Parked && !one.Held).OrderBy(one => one.Age))
        {
            if (projected <= fanLine)
            {
                break;
            }

            steps.Add(picture.Asset);
            projected -= Saving(picture);
        }

        if (projected <= cap)
        {
            return steps;
        }

        foreach (var picture in pictures.Where(one => !one.Parked && !one.Held).OrderBy(one => one.Age))
        {
            if (projected <= cap)
            {
                break;
            }

            steps.Add(picture.Asset);
            projected -= Saving(picture);
        }

        return steps;
    }
}
