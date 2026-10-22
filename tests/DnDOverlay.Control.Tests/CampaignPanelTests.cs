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

    /// <summary>
    /// A picture pulled in that is already in the stock: the stock narrows to it and picks it out
    /// (Part 7: "filters to this picture, highlights it briefly").
    /// </summary>
    [Fact]
    public async Task A_picture_already_in_the_stock_is_shown_there()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var known = await bench.PictureAsync("Grimmbart", 3);

            await bench.PictureAsync("Ork", 4);

            var file = Path.Combine(bench.Base, "ork_04.png");

            await File.WriteAllBytesAsync(file, CampaignBench.Png(3), Cancellation);

            var parts = bench.Parts();
            var panel = new CampaignPanel(new PanelHead(), parts.Stock, parts.Scenes, parts.Layouts, parts.Intake, bench.Settings);

            await parts.Intake.RunAsync(parts.Intake.Entrances.FromFiles([file]), target: null);

            Assert.Equal([known.AssetId], parts.Stock.Narrowing);
            Assert.Single(StockViewTests.Cards(parts.Stock));
            Assert.Equal([known], parts.Stock.Chosen);
            GC.KeepAlive(panel);
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

    /// <summary>
    /// With no campaign open and the campaigns folder out of reach, the head says so in place of the
    /// two ways on - both would lead to an empty switcher (Part 3). An open campaign hides it.
    /// </summary>
    [Fact]
    public async Task An_unreachable_campaigns_folder_is_said_in_the_head()
    {
        using var thread = new DispatcherThread();

        await thread.On(() =>
        {
            var head = new PanelHead();

            head.Campaign(label: null, unreachable: "Der Stick fehlt");

            Assert.Contains(Texts(head), text => text.StartsWith(Strings.PanelHeadUnreachable, StringComparison.Ordinal));
            Assert.Contains("Der Stick fehlt", Texts(head));

            head.Campaign(label: null);

            Assert.DoesNotContain("Der Stick fehlt", Texts(head));
            Assert.Contains(Strings.PanelHeadHint, Texts(head));

            return Task.CompletedTask;
        });
    }

    /// <summary>Every text the head shows: the labels of its buttons and its text blocks.</summary>
    private static List<string> Texts(System.Windows.DependencyObject element)
    {
        var texts = new List<string>();

        if (element is System.Windows.Controls.TextBlock { Visibility: System.Windows.Visibility.Visible } block)
        {
            texts.Add(block.Text);
        }

        if (element is System.Windows.Controls.ContentControl { Content: string content })
        {
            texts.Add(content);
        }

        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(element).OfType<System.Windows.DependencyObject>())
        {
            texts.AddRange(Texts(child));
        }

        return texts;
    }

    private static (CampaignPanel Panel, StockView Stock) Panel(CampaignBench bench)
    {
        var parts = bench.Parts();

        return (new CampaignPanel(new PanelHead(), parts.Stock, parts.Scenes, parts.Layouts, parts.Intake, bench.Settings), parts.Stock);
    }
}
