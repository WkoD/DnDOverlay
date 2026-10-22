namespace DnDOverlay.Core.Configuration;

/// <summary>The two things the stock reports about the open campaign (Part 7).</summary>
public enum StockTrigger
{
    /// <summary>Free space on the campaign drive is running out - the next intake would fail mid-preparation.</summary>
    FreeSpace,

    /// <summary>The open campaign holds so many pictures that grid, filter and folder check grow slow.</summary>
    Pictures,
}

/// <summary>
/// When the stock speaks up, and what the DM has already heard (Part 7). Kept in <c>control.json</c>:
/// the thresholds, a switch per trigger, and the value each was last acknowledged at.
/// <para>
/// <b>Once per start, and after that only when it is noticeably worse</b> - by another
/// <see cref="Step"/>. Without that a DM with a rightly large stock would see the notice every
/// evening and switch it off after the third, and with it the warning about the disk.
/// </para>
/// </summary>
/// <param name="FreeSpaceBelowBytes">Reported when the campaign drive has less than this free. A proposal (Part 7: 5 GB).</param>
/// <param name="PicturesAbove">Reported when the open campaign holds more pictures than this. A starting point, measured by step 28 (Part 7: 2000).</param>
/// <param name="FreeSpaceOn">"Don't report again" for free space switches this off; so does the box in the settings.</param>
/// <param name="PicturesOn">The same for the number of pictures.</param>
/// <param name="FreeSpaceAcknowledged">The free space the last notice was clicked away at, or <see langword="null"/>.</param>
/// <param name="PicturesAcknowledged">The number of pictures the last notice was clicked away at, or <see langword="null"/>.</param>
public sealed record StockReporting(
    long FreeSpaceBelowBytes = StockReporting.DefaultFreeSpaceBelowBytes,
    int PicturesAbove = StockReporting.DefaultPicturesAbove,
    bool FreeSpaceOn = true,
    bool PicturesOn = true,
    long? FreeSpaceAcknowledged = null,
    int? PicturesAcknowledged = null)
{
    /// <summary>5 GB - below it the next intake may fail in the middle of preparing (Part 7).</summary>
    public const long DefaultFreeSpaceBelowBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>2000 - where grid, filter, the inventory in memory and the folder check start to weigh (Part 7).</summary>
    public const int DefaultPicturesAbove = 2000;

    /// <summary>How much worse than the acknowledged value it has to get before the notice comes again (Part 7: 25 %).</summary>
    public const double Step = 0.25;

    /// <summary>
    /// The triggers that speak now, for the open campaign: over the threshold, switched on, not yet
    /// spoken in this run, and either never acknowledged or worse by another <see cref="Step"/>
    /// since. Measured only when a campaign opens - in the middle of a session nothing speaks.
    /// </summary>
    /// <param name="freeBytes">Free space on the drive the campaigns lie on.</param>
    /// <param name="pictures">Pictures in the campaign just opened.</param>
    /// <param name="spoken">The triggers that already spoke in this run.</param>
    public IReadOnlyList<StockTrigger> Due(long freeBytes, int pictures, IReadOnlySet<StockTrigger> spoken)
    {
        ArgumentNullException.ThrowIfNull(spoken);

        var due = new List<StockTrigger>(2);

        if (FreeSpaceOn
            && !spoken.Contains(StockTrigger.FreeSpace)
            && freeBytes < FreeSpaceBelowBytes
            && (FreeSpaceAcknowledged is not { } heard || freeBytes <= heard * (1 - Step)))
        {
            due.Add(StockTrigger.FreeSpace);
        }

        if (PicturesOn
            && !spoken.Contains(StockTrigger.Pictures)
            && pictures > PicturesAbove
            && (PicturesAcknowledged is not { } counted || pictures >= counted * (1 + Step)))
        {
            due.Add(StockTrigger.Pictures);
        }

        return due;
    }
}
