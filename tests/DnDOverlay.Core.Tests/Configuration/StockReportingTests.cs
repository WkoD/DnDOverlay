using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Core.Tests.Configuration;

/// <summary>When the stock speaks up (Part 7): over a threshold, once per start, again only when it got worse.</summary>
public sealed class StockReportingTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    private static readonly HashSet<StockTrigger> Nothing = [];

    [Fact]
    public void Below_the_thresholds_nothing_speaks()
    {
        Assert.Empty(new StockReporting().Due(50 * Gigabyte, 500, Nothing));
    }

    [Fact]
    public void Each_trigger_speaks_on_its_own()
    {
        var reporting = new StockReporting();

        Assert.Equal([StockTrigger.FreeSpace], reporting.Due(3 * Gigabyte, 500, Nothing));
        Assert.Equal([StockTrigger.Pictures], reporting.Due(50 * Gigabyte, 2001, Nothing));
        Assert.Equal([StockTrigger.FreeSpace, StockTrigger.Pictures], reporting.Due(3 * Gigabyte, 2001, Nothing));
    }

    /// <summary>Once per start: a second campaign opened in the same run does not repeat it.</summary>
    [Fact]
    public void A_trigger_speaks_once_per_run()
    {
        Assert.Empty(new StockReporting().Due(3 * Gigabyte, 2500, new HashSet<StockTrigger> { StockTrigger.FreeSpace, StockTrigger.Pictures }));
    }

    /// <summary>Acknowledged, it comes again only once it is worse by another quarter.</summary>
    [Fact]
    public void An_acknowledged_value_speaks_again_only_a_step_worse()
    {
        var reporting = new StockReporting(FreeSpaceAcknowledged: 4 * Gigabyte, PicturesAcknowledged: 2400);

        Assert.Empty(reporting.Due(4 * Gigabyte - 1, 2999, Nothing));
        Assert.Equal([StockTrigger.FreeSpace, StockTrigger.Pictures], reporting.Due(3 * Gigabyte, 3000, Nothing));
    }

    /// <summary>"Don't report again" is per trigger: the disk still warns when the count is switched off.</summary>
    [Fact]
    public void Switching_one_off_leaves_the_other()
    {
        Assert.Equal([StockTrigger.FreeSpace], new StockReporting(PicturesOn: false).Due(3 * Gigabyte, 5000, Nothing));
    }
}
