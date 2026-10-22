using System.IO;
using DnDOverlay.Campaign;
using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>The one path every entrance ends on (Part 7).</summary>
public sealed class IntakeRunnerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// A picture taken in a second time gets no second entry - it gets shown: the run names the entry
    /// that was there, under the name it has (Part 7), for the stock to narrow to it.
    /// </summary>
    [Fact]
    public async Task A_picture_already_in_the_stock_is_handed_on_to_be_shown()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var known = await bench.PictureAsync("Grimmbart", 3);
            var file = Path.Combine(bench.Base, "ork_04.png");

            await File.WriteAllBytesAsync(file, CampaignBench.Png(3), Cancellation);

            var intake = bench.Parts().Intake;
            IReadOnlyList<AssetRef>? shown = null;

            intake.AlreadyThere += (_, assets) => shown = assets;
            await intake.RunAsync(intake.Entrances.FromFiles([file]), target: null);

            var asset = Assert.Single(shown!);

            Assert.Equal(known.AssetId, asset.AssetId);
            Assert.Equal("Grimmbart", asset.Name);
        });
    }

    /// <summary>
    /// A copied page whose fragment links to a file on this machine: the link names what the page
    /// points to, not the copied picture. The offer's own bitmap comes in - not the linked file, which
    /// files-first would otherwise put ahead of it.
    /// </summary>
    [Fact]
    public async Task A_link_on_a_copied_page_does_not_win_over_its_picture()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var linked = Path.Combine(bench.Base, "notizen.png");
            var copied = CampaignBench.Png(2);

            await File.WriteAllBytesAsync(linked, CampaignBench.Png(1), Cancellation);

            var data = new System.Windows.DataObject();

            data.SetData(System.Windows.DataFormats.Html, $"<a href=\"{new Uri(linked).AbsoluteUri}\">Notizen</a>");
            data.SetData("PNG", new MemoryStream(copied));

            var source = Assert.Single(bench.Parts().Intake.Entrances.FromDataObject(data));
            var bytes = Assert.IsType<IntakeBytes.Ready>(await source.Open(Cancellation));

            Assert.Equal(copied, bytes.Data.ToArray());
        });
    }
}
