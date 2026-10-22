using System.IO;
using System.Windows;
using DnDOverlay.Campaign;
using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The four ways a picture gets in (Part 5, Part 7), as far as the Windows side decides: what a drop or
/// a paste offers becomes sources in one order, and the counted name advances only when it was used.
/// </summary>
public sealed class EntrancesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// A folder is walked to its bottom - check step 29a drags a folder, and NPC pictures sit in
    /// subfolders; a list that kept only files would have taken in nothing (Part 7).
    /// </summary>
    [Fact]
    public async Task A_dropped_folder_brings_every_file_beneath_it()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var folder = Path.Combine(bench.Base, "Bilder");

            Directory.CreateDirectory(Path.Combine(folder, "NSC"));
            await File.WriteAllBytesAsync(Path.Combine(folder, "karte.png"), CampaignBench.Png(1), Cancellation);
            await File.WriteAllBytesAsync(Path.Combine(folder, "NSC", "grimmbart.png"), CampaignBench.Png(2), Cancellation);

            var sources = Entrances(bench).FromFiles([folder]);

            Assert.Equal(2, sources.Count);
        });
    }

    /// <summary>A path that is neither a file nor a folder - gone since it was chosen - brings nothing and breaks nothing.</summary>
    [Fact]
    public async Task A_path_that_is_gone_brings_nothing()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            Assert.Empty(Entrances(bench).FromFiles([Path.Combine(bench.Base, "weg.png"), Path.Combine(bench.Base, "weg")]));

            await Task.CompletedTask;
        });
    }

    /// <summary>
    /// A file brings its own name, and a name that did not fall through to the counter leaves it
    /// alone: two hundred named files must not run the counter up for nothing.
    /// </summary>
    [Fact]
    public async Task A_named_file_does_not_advance_the_counter()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var file = Path.Combine(bench.Base, "grimmbart.png");

            await File.WriteAllBytesAsync(file, CampaignBench.Png(1), Cancellation);

            var source = Assert.Single(Entrances(bench).FromFiles([file]));

            Assert.Equal(AssetNaming.Derive(new NameSource { FileName = "grimmbart.png" }, "Clipboard {n}", 1), source.ProposedName);
            Assert.Equal(1, bench.Settings.Current.CountedNameNext);
        });
    }

    /// <summary>
    /// A screenshot has nothing to derive a name from: it gets the counted name, and the counter moves
    /// on and is kept, so the next session does not begin at one again (Part 3).
    /// </summary>
    [Fact]
    public async Task A_pasted_screenshot_takes_the_counted_name_and_advances_it()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var data = new DataObject();

            data.SetData("PNG", new MemoryStream(CampaignBench.Png(1)));

            var first = Assert.Single(Entrances(bench).FromDataObject(data));
            var second = Assert.Single(Entrances(bench).FromDataObject(data));

            Assert.Equal("Clipboard 1", first.ProposedName);
            Assert.Equal("Clipboard 2", second.ProposedName);
            Assert.Equal(3, bench.Settings.Current.CountedNameNext);

            await Task.CompletedTask;
        });
    }

    /// <summary>Files come first (Part 7): a drop carrying files and a bitmap takes the files.</summary>
    [Fact]
    public async Task Dropped_files_win_over_a_bitmap_beside_them()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var file = Path.Combine(bench.Base, "karte.png");
            var dropped = CampaignBench.Png(1);

            await File.WriteAllBytesAsync(file, dropped, Cancellation);

            var data = new DataObject();

            data.SetData(DataFormats.FileDrop, new[] { file });
            data.SetData("PNG", new MemoryStream(CampaignBench.Png(2)));

            var source = Assert.Single(Entrances(bench).FromDataObject(data));
            var bytes = Assert.IsType<IntakeBytes.Ready>(await source.Open(Cancellation));

            Assert.Equal(dropped, bytes.Data.ToArray());
        });
    }

    /// <summary>
    /// An address naming an existing file on this machine is a file (VS Code's Markdown preview hands a
    /// picture over by nothing else) - read from disk, not fetched.
    /// </summary>
    [Fact]
    public async Task An_address_naming_a_file_here_is_taken_as_that_file()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var file = Path.Combine(bench.Base, "grimmbart.png");
            var picture = CampaignBench.Png(3);

            await File.WriteAllBytesAsync(file, picture, Cancellation);

            var data = new DataObject();

            data.SetData(DataFormats.UnicodeText, new Uri(file).AbsoluteUri);

            var source = Assert.Single(Entrances(bench).FromDataObject(data));
            var bytes = Assert.IsType<IntakeBytes.Ready>(await source.Open(Cancellation));

            Assert.Equal(picture, bytes.Data.ToArray());
        });
    }

    /// <summary>Only http and https are fetched; an offer of words or of another scheme brings nothing.</summary>
    [Theory]
    [InlineData("Grimmbart der Zwerg")]
    [InlineData("ftp://example.org/karte.png")]
    [InlineData("javascript:alert(1)")]
    public async Task Text_that_is_no_web_address_brings_nothing(string text)
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var data = new DataObject();

            data.SetData(DataFormats.UnicodeText, text);

            Assert.Empty(Entrances(bench).FromDataObject(data));

            await Task.CompletedTask;
        });
    }

    /// <summary>A web address becomes one source, fetched only when its turn comes - nothing is fetched here.</summary>
    [Fact]
    public async Task A_web_address_becomes_one_source_named_from_it()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var data = new DataObject();

            data.SetData(DataFormats.UnicodeText, "https://example.org/bilder/grimmbart.png");

            var source = Assert.Single(Entrances(bench).FromDataObject(data));

            Assert.Equal(
                AssetNaming.Derive(new NameSource { Text = "https://example.org/bilder/grimmbart.png", SourceUrl = "https://example.org/bilder/grimmbart.png" }, "Clipboard {n}", 1),
                source.ProposedName);

            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task An_empty_offer_brings_nothing()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            Assert.Empty(Entrances(bench).FromDataObject(new DataObject()));
            Assert.Empty(Entrances(bench).FromDataObject(null!));
            Assert.Equal("(nothing)", Control.Entrances.Formats(null!));

            await Task.CompletedTask;
        });
    }

    private static Entrances Entrances(CampaignBench bench) => new(bench.Stock, bench.Settings, TimeProvider.System);
}
