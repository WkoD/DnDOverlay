using System.IO;
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
}
