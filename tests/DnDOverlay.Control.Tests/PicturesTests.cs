using System.IO;
using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The pictures the stage draws, kept once per asset (Part 7): the preview, never the original, and a
/// failure remembered so sixty frames a second do not each try the same missing file.
/// </summary>
public sealed class PicturesTests
{
    [Fact]
    public async Task A_preview_is_loaded_once_and_kept()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var asset = await bench.PictureAsync("Krieger", 1);
            var pictures = new Pictures(bench.Stock);

            var first = pictures.For(asset.AssetId);

            Assert.NotNull(first);
            Assert.Same(first, pictures.For(asset.AssetId));
            Assert.Equal(1, pictures.Loaded);
        });
    }

    /// <summary>
    /// A thumbnail that is not there is a failure, and it is remembered as one: the file appearing a
    /// frame later is read only once the asset is forgotten - which is what a rename or a fresh intake
    /// asks for.
    /// </summary>
    [Fact]
    public async Task A_missing_preview_is_remembered_until_the_asset_is_forgotten()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var asset = await bench.PictureAsync("Krieger", 1);
            var pictures = new Pictures(bench.Stock);
            var thumbnail = bench.Stock.Current!.ThumbnailPath(asset.AssetId);
            var aside = thumbnail + ".aside";

            File.Move(thumbnail, aside);

            Assert.Null(pictures.For(asset.AssetId));

            File.Move(aside, thumbnail);

            Assert.Null(pictures.For(asset.AssetId));

            pictures.Forget(asset.AssetId);

            Assert.NotNull(pictures.For(asset.AssetId));
        });
    }

    /// <summary>The grey version for what has not reached the table yet - none where there is no colour one.</summary>
    [Fact]
    public async Task Grey_follows_the_colour_preview()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var asset = await bench.PictureAsync("Krieger", 1);
            var pictures = new Pictures(bench.Stock);
            var unknown = new AssetId(new string('c', 64));

            var grey = pictures.Grey(asset.AssetId);

            Assert.NotNull(grey);
            Assert.Equal(System.Windows.Media.PixelFormats.Gray8, grey.Format);
            Assert.Same(grey, pictures.Grey(asset.AssetId));
            Assert.Null(pictures.Grey(unknown));
            Assert.Equal(2, pictures.Greyed);
        });
    }

    /// <summary>Another campaign's pictures are other files: a clear forgets every preview, failures included.</summary>
    [Fact]
    public async Task Clear_forgets_everything()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var asset = await bench.PictureAsync("Krieger", 1);
            var pictures = new Pictures(bench.Stock);

            _ = pictures.Grey(asset.AssetId);
            _ = pictures.For(new AssetId(new string('c', 64)));

            pictures.Clear();

            Assert.Equal(0, pictures.Loaded);
            Assert.Equal(0, pictures.Greyed);
        });
    }

    /// <summary>With no campaign open there is no stock to read from: nothing is drawn, and nothing breaks.</summary>
    [Fact]
    public async Task Without_an_open_stock_there_is_no_preview()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var pictures = new Pictures(bench.Stock);

            Assert.Null(pictures.For(new AssetId(new string('c', 64))));

            await Task.CompletedTask;
        });
    }
}
