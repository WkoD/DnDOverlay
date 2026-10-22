using System.IO;
using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>The campaign panel (Part 7): its tabs, its search bar, and the intake beside them.</summary>
public sealed class CampaignPanelTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// "Show in stock" narrows the grid to one picture of the open campaign. In the next campaign that
    /// narrowing means nothing - it goes with the switch, or the stock would say "no match" until the
    /// search box was cleared by hand.
    /// </summary>
    [Fact]
    public async Task A_narrowing_to_one_picture_goes_with_a_campaign_switch()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var picture = await bench.PictureAsync("Krieger", 1);
            var (panel, stock) = Panel(bench);

            panel.ShowInStock(picture.AssetId, picture.Name);

            Assert.NotNull(stock.Narrowing);

            await bench.Desk.SwitchAsync(bench.Campaign("Tyranny"));

            Assert.Null(stock.Narrowing);
        });
    }

    /// <summary>While pictures come in, the desk knows it - and refuses to switch, rename or delete under them.</summary>
    [Fact]
    public async Task The_desk_knows_while_an_intake_runs()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var file = Path.Combine(bench.Base, "neu.png");

            await File.WriteAllBytesAsync(file, CampaignBench.Png(5), Cancellation);

            var parts = bench.Parts();
            var panel = new CampaignPanel(new PanelHead(), parts.Stock, parts.Scenes, parts.Layouts, parts.Intake, bench.Settings);
            var during = false;

            parts.Intake.RunningChanged += (_, running) => during |= running && bench.Desk.Taking;

            await parts.Intake.RunAsync(parts.Intake.Entrances.FromFiles([file]), target: null);

            Assert.True(during);
            Assert.False(bench.Desk.Taking);
            GC.KeepAlive(panel);
        });
    }

    private static (CampaignPanel Panel, StockView Stock) Panel(CampaignBench bench)
    {
        var parts = bench.Parts();

        return (new CampaignPanel(new PanelHead(), parts.Stock, parts.Scenes, parts.Layouts, parts.Intake, bench.Settings), parts.Stock);
    }
}
