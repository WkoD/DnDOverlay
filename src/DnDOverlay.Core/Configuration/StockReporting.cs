namespace DnDOverlay.Core.Configuration;

/// <summary>The two things the stock reports about the open campaign (Part 7).</summary>
public enum StockTrigger
{
    /// <summary>Free space on the campaign drive is running out - the next intake would fail mid-preparation.</summary>
    FreeSpace,

    /// <summary>The open campaign holds so many pictures that grid, filter and folder check grow slow.</summary>
    Pictures,
}

/// <summary>The number of pictures the notice was clicked away at for one campaign (Part 7).</summary>
/// <param name="Campaign">The campaign's name - which is also its folder's (Part 3).</param>
/// <param name="Pictures">How many pictures it held when the DM acknowledged the notice.</param>
public sealed record PicturesHeard(string Campaign, int Pictures);

/// <summary>
/// When the stock speaks up, and what the DM has already heard (Part 7). Kept in <c>control.json</c>:
/// the thresholds, a switch per trigger, and the value each was last acknowledged at.
/// <para>
/// <b>Once per start, and after that only when it is noticeably worse</b> - by another
/// <see cref="Step"/>. Without that a DM with a rightly large stock would see the notice every
/// evening and switch it off after the third, and with it the warning about the disk.
/// </para>
/// <para>
/// <b>The number of pictures is acknowledged per campaign, the free space once.</b> The count belongs
/// to the campaign it was measured in: acknowledged as one value for all, a second campaign with a
/// large stock stayed silent for good once the first one's notice had been clicked away at a higher
/// count (M5 review). The free space belongs to the drive, which every campaign shares.
/// </para>
/// </summary>
/// <param name="FreeSpaceBelowBytes">Reported when the campaign drive has less than this free. A proposal (Part 7: 5 GB).</param>
/// <param name="PicturesAbove">Reported when the open campaign holds more pictures than this. A starting point, measured by step 28 (Part 7: 2000).</param>
/// <param name="FreeSpaceOn">"Don't report again" for free space switches this off; so does the box in the settings.</param>
/// <param name="PicturesOn">The same for the number of pictures.</param>
/// <param name="FreeSpaceAcknowledged">The free space the last notice was clicked away at, or <see langword="null"/>.</param>
/// <param name="PicturesAcknowledgedIn">
/// Per campaign, the number of pictures its last notice was clicked away at; <see langword="null"/>
/// or a campaign not listed: never acknowledged. A file written before this list - with one shared
/// <c>picturesAcknowledged</c> - still loads: that value is left unread, because nobody can say which
/// campaign it was clicked away in, and a notice once more is the price of not guessing.
/// </param>
/// <param name="MovedUnverified">
/// Campaigns copied to a new place by a move of the campaign folder and not yet offered "Verify stock"
/// (Part 7: a copy is checked by number and size, not content - whoever wants to know exactly is
/// offered the verify after the move). Each is offered once, at its next opening - which may be
/// evenings later, so it is kept here and not in memory.
/// </param>
public sealed record StockReporting(
    long FreeSpaceBelowBytes = StockReporting.DefaultFreeSpaceBelowBytes,
    int PicturesAbove = StockReporting.DefaultPicturesAbove,
    bool FreeSpaceOn = true,
    bool PicturesOn = true,
    long? FreeSpaceAcknowledged = null,
    IReadOnlyList<PicturesHeard>? PicturesAcknowledgedIn = null,
    IReadOnlyList<string>? MovedUnverified = null)
{
    /// <summary>5 GB - below it the next intake may fail in the middle of preparing (Part 7).</summary>
    public const long DefaultFreeSpaceBelowBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>2000 - where grid, filter, the inventory in memory and the folder check start to weigh (Part 7).</summary>
    public const int DefaultPicturesAbove = 2000;

    /// <summary>How much worse than the acknowledged value it has to get before the notice comes again (Part 7: 25 %).</summary>
    public const double Step = 0.25;

    /// <summary>
    /// The triggers that speak now, for the campaign just opened: over the threshold, switched on, not
    /// yet spoken in this run, and either never acknowledged or worse by another <see cref="Step"/>
    /// since - the picture count against what was acknowledged in <b>this</b> campaign. Measured only
    /// when a campaign opens - in the middle of a session nothing speaks.
    /// </summary>
    /// <param name="freeBytes">Free space on the drive the campaigns lie on.</param>
    /// <param name="pictures">Pictures in the campaign just opened.</param>
    /// <param name="campaign">The name of the campaign just opened.</param>
    /// <param name="spoken">The triggers that already spoke in this run.</param>
    public IReadOnlyList<StockTrigger> Due(long freeBytes, int pictures, string campaign, IReadOnlySet<StockTrigger> spoken)
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
            && (PicturesAcknowledgedFor(campaign) is not { } counted || pictures >= counted * (1 + Step)))
        {
            due.Add(StockTrigger.Pictures);
        }

        return due;
    }

    /// <summary>The number of pictures the notice was last clicked away at in this campaign, or <see langword="null"/>.</summary>
    public int? PicturesAcknowledgedFor(string campaign) =>
        PicturesAcknowledgedIn?.LastOrDefault(heard => Same(heard.Campaign, campaign))?.Pictures;

    /// <summary>The same, with this campaign's count acknowledged at <paramref name="pictures"/> - the other campaigns keep theirs.</summary>
    public StockReporting AcknowledgePictures(string campaign, int pictures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaign);

        return this with
        {
            PicturesAcknowledgedIn =
            [
                .. (PicturesAcknowledgedIn ?? []).Where(heard => !Same(heard.Campaign, campaign)),
                new PicturesHeard(campaign, pictures),
            ],
        };
    }

    /// <summary>The same, with these campaigns copied by a move and their verify still to be offered.</summary>
    public StockReporting Moved(IEnumerable<string> campaigns)
    {
        ArgumentNullException.ThrowIfNull(campaigns);

        var waiting = (MovedUnverified ?? []).ToList();

        waiting.AddRange(campaigns.Where(campaign => !waiting.Any(known => Same(known, campaign))));

        return this with { MovedUnverified = waiting };
    }

    /// <summary>Whether this campaign was copied by a move and has not been offered "Verify stock" since.</summary>
    public bool VerifyDue(string campaign) => MovedUnverified?.Any(moved => Same(moved, campaign)) is true;

    /// <summary>The same, with this campaign's verify offered - once is the offer, the answer is the DM's.</summary>
    public StockReporting VerifyOffered(string campaign) =>
        this with { MovedUnverified = [.. (MovedUnverified ?? []).Where(moved => !Same(moved, campaign))] };

    /// <summary>Campaign names name folders, and Windows does not tell <c>Sturmküste</c> from <c>sturmküste</c>.</summary>
    private static bool Same(string? one, string? other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
}
