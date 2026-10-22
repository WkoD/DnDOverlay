using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Core.Tests.Configuration;

/// <summary>When the stock speaks up (Part 7): over a threshold, once per start, again only when it got worse.</summary>
public sealed class StockReportingTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    private const string Campaign = "Sturmküste";

    private static readonly HashSet<StockTrigger> Nothing = [];

    [Fact]
    public void Below_the_thresholds_nothing_speaks()
    {
        Assert.Empty(new StockReporting().Due(50 * Gigabyte, 500, Campaign, Nothing));
    }

    [Fact]
    public void Each_trigger_speaks_on_its_own()
    {
        var reporting = new StockReporting();

        Assert.Equal([StockTrigger.FreeSpace], reporting.Due(3 * Gigabyte, 500, Campaign, Nothing));
        Assert.Equal([StockTrigger.Pictures], reporting.Due(50 * Gigabyte, 2001, Campaign, Nothing));
        Assert.Equal([StockTrigger.FreeSpace, StockTrigger.Pictures], reporting.Due(3 * Gigabyte, 2001, Campaign, Nothing));
    }

    /// <summary>Once per start: a second campaign opened in the same run does not repeat it.</summary>
    [Fact]
    public void A_trigger_speaks_once_per_run()
    {
        Assert.Empty(new StockReporting().Due(3 * Gigabyte, 2500, Campaign, new HashSet<StockTrigger> { StockTrigger.FreeSpace, StockTrigger.Pictures }));
    }

    /// <summary>Acknowledged, it comes again only once it is worse by another quarter.</summary>
    [Fact]
    public void An_acknowledged_value_speaks_again_only_a_step_worse()
    {
        var reporting = new StockReporting(FreeSpaceAcknowledged: 4 * Gigabyte).AcknowledgePictures(Campaign, 2400);

        Assert.Empty(reporting.Due(4 * Gigabyte - 1, 2999, Campaign, Nothing));
        Assert.Equal([StockTrigger.FreeSpace, StockTrigger.Pictures], reporting.Due(3 * Gigabyte, 3000, Campaign, Nothing));
    }

    /// <summary>
    /// The count acknowledged in one campaign says nothing about another: a second large campaign speaks
    /// at its own count, not only once it passes the first one's by a quarter (M5 review).
    /// </summary>
    [Fact]
    public void The_picture_count_is_acknowledged_per_campaign()
    {
        var reporting = new StockReporting().AcknowledgePictures(Campaign, 4000);

        Assert.Empty(reporting.Due(50 * Gigabyte, 4500, Campaign, Nothing));
        Assert.Equal([StockTrigger.Pictures], reporting.Due(50 * Gigabyte, 2500, "Vinland", Nothing));
        Assert.Empty(reporting.Due(50 * Gigabyte, 4500, "sturmküste", Nothing));
    }

    /// <summary>Acknowledging one campaign again replaces its value and keeps the others'.</summary>
    [Fact]
    public void Acknowledging_again_replaces_only_that_campaign()
    {
        var reporting = new StockReporting()
            .AcknowledgePictures(Campaign, 2400)
            .AcknowledgePictures("Vinland", 3000)
            .AcknowledgePictures(Campaign, 2600);

        Assert.Equal(2600, reporting.PicturesAcknowledgedFor(Campaign));
        Assert.Equal(3000, reporting.PicturesAcknowledgedFor("Vinland"));
        Assert.Null(reporting.PicturesAcknowledgedFor("Tyranny"));
        Assert.Equal(2, reporting.PicturesAcknowledgedIn!.Count);
    }

    /// <summary>The free space belongs to the drive: acknowledged once, for every campaign.</summary>
    [Fact]
    public void The_free_space_is_acknowledged_for_every_campaign()
    {
        var reporting = new StockReporting(FreeSpaceAcknowledged: 4 * Gigabyte);

        Assert.Empty(reporting.Due(4 * Gigabyte - 1, 500, "Vinland", Nothing));
    }

    /// <summary>"Don't report again" is per trigger: the disk still warns when the count is switched off.</summary>
    [Fact]
    public void Switching_one_off_leaves_the_other()
    {
        Assert.Equal([StockTrigger.FreeSpace], new StockReporting(PicturesOn: false).Due(3 * Gigabyte, 5000, Campaign, Nothing));
    }
}
