using System.Text;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// Campaigns under a base path and the reconciliation of a folder with its inventory (Part 3):
/// derived things repaired without a word, primary things reported, nothing deleted unasked.
/// </summary>
public sealed class CampaignLibraryTests : IDisposable
{
    private readonly string _base =
        Path.Combine(Path.GetTempPath(), "dndoverlay-campaigns-" + Guid.NewGuid().ToString("N"));

    private readonly FakeImageCodec _codec = new();
    private readonly FakeTimeProvider _time = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_base, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    /// <summary>Nothing is created by looking: no base folder, no campaign, until a first picture asks for one.</summary>
    [Fact]
    public void Listing_an_empty_base_creates_nothing()
    {
        Assert.Empty(Library().List());
        Assert.False(Directory.Exists(_base));
    }

    /// <summary>An unnamed campaign that exists is not reused - the next one gets the next number.</summary>
    [Fact]
    public void A_new_unnamed_campaign_takes_the_next_number()
    {
        var library = Library();

        var first = library.CreateUnnamed();
        var second = library.CreateUnnamed();

        Assert.Equal("~unbenannt", first.Name);
        Assert.Equal("~unbenannt 2", second.Name);
        Assert.True(first.Unnamed);
    }

    /// <summary>Unnamed ones are told apart by date and count; named ones list by name and nothing else.</summary>
    [Fact]
    public async Task The_list_names_campaigns_and_dates_and_counts_only_the_unnamed()
    {
        var library = Library();
        var unnamed = library.CreateUnnamed();

        await Ingest(unnamed.Store, "eins");
        Directory.CreateDirectory(Path.Combine(_base, "Sturmküste"));

        var list = library.List();

        Assert.Equal("Sturmküste", list[0].Name);
        Assert.Null(list[0].Pictures);
        Assert.Equal(1, Assert.Single(list, summary => summary.Unnamed).Pictures);
    }

    [Fact]
    public void Renaming_moves_the_folder_and_a_taken_name_is_refused_first()
    {
        var library = Library();
        var campaign = library.CreateUnnamed();

        Directory.CreateDirectory(Path.Combine(_base, "Sturmküste"));

        Assert.Equal(CampaignOutcome.Taken, library.Rename(campaign, "STURMKÜSTE").Outcome);
        Assert.Equal(CampaignOutcome.BadName, library.Rename(campaign, "Rat 3/4").Outcome);

        var (outcome, renamed) = library.Rename(campaign, "Kerkerwelt");

        Assert.Equal(CampaignOutcome.Done, outcome);
        Assert.Equal("Kerkerwelt", renamed.Name);
        Assert.False(renamed.Unnamed);
        Assert.True(Directory.Exists(Path.Combine(_base, "Kerkerwelt")));
        Assert.False(Directory.Exists(Path.Combine(_base, "~unbenannt")));
    }

    [Fact]
    public void Deleting_takes_the_folder()
    {
        var library = Library();
        var campaign = library.CreateUnnamed();

        library.Delete(campaign);

        Assert.Empty(library.List());
    }

    /// <summary>A loose picture in the root and one in a folder of the DM's own come in, renamed to their hash and moved; the originals are gone.</summary>
    [Fact]
    public async Task Loose_pictures_anywhere_in_the_folder_are_taken_in()
    {
        var campaign = Campaign("Sturmküste");

        _codec.PassThrough = true;

        Directory.CreateDirectory(Path.Combine(campaign, "Meine Bilder"));
        File.WriteAllBytes(Path.Combine(campaign, "Ork.png"), Bytes("ork"));
        File.WriteAllBytes(Path.Combine(campaign, "Meine Bilder", "Goblin.png"), Bytes("goblin"));

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal(2, report.Adopted);
        Assert.Equal(["Goblin", "Ork"], opened.Store.Entries.Select(entry => entry.Name).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(Path.Combine(campaign, "Ork.png")));
        Assert.Empty(Reconciliation.Survey(opened.Store).Orphans);
    }

    [Fact]
    public async Task A_byte_identical_copy_is_removed_rather_than_kept_twice()
    {
        var campaign = Campaign("Sturmküste");
        var library = Library();

        _codec.PassThrough = true;

        await Ingest(library.Open("Sturmküste").Store, "ork");
        File.WriteAllBytes(Path.Combine(campaign, "Ork Kopie.png"), Bytes("ork"));

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal(1, report.Duplicates);
        Assert.Single(opened.Store.Entries);
        Assert.False(File.Exists(Path.Combine(campaign, "Ork Kopie.png")));
    }

    /// <summary>
    /// A picture converted on the way in - a WebP that became a PNG, an animation that became a GIF - is
    /// taken in, and its original STAYS: what lies in assets\ is not that file, and Part 3 removes
    /// byte-identical copies only. On the next opening it is neither adopted again nor called a duplicate.
    /// </summary>
    [Fact]
    public async Task A_picture_converted_on_the_way_in_keeps_its_original()
    {
        var campaign = Campaign("Sturmküste");
        var original = Path.Combine(campaign, "Drache.webp");

        File.WriteAllBytes(original, Bytes("drache"));

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal(1, report.Adopted);
        Assert.Equal(["Drache.webp"], report.Kept);
        Assert.Equal("Drache", Assert.Single(opened.Store.Entries).Name);
        Assert.Equal(Bytes("drache"), File.ReadAllBytes(original));

        var (_, again) = await OpenAndSettle("Sturmküste");

        Assert.Equal(0, again.Adopted);
        Assert.Equal(0, again.Duplicates);
        Assert.Equal(["Drache.webp"], again.Kept);
        Assert.True(File.Exists(original));
    }

    /// <summary>
    /// The picture coming back heals its entry, and that is counted as taken in - not as "a duplicate
    /// removed", which is what the report said while the healing branch answered "already present".
    /// </summary>
    [Fact]
    public async Task A_picture_laid_back_into_the_folder_heals_its_entry_and_counts_as_taken_in()
    {
        var campaign = Campaign("Sturmküste");
        var library = Library();

        _codec.PassThrough = true;

        var taken = await Ingest(library.Open("Sturmküste").Store, "ork");

        foreach (var file in Directory.GetFiles(Path.Combine(campaign, "assets"), taken.Asset.AssetId.Value + ".*"))
        {
            File.Delete(file);
        }

        File.WriteAllBytes(Path.Combine(campaign, "Ork.png"), Bytes("ork"));

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal(1, report.Adopted);
        Assert.Equal(0, report.Duplicates);
        Assert.Empty(report.Missing);
        Assert.Empty(opened.Store.Missing);
        Assert.False(File.Exists(Path.Combine(campaign, "Ork.png")));
    }

    /// <summary>
    /// A hash name is no pass: a file named like one that is no picture is checked where it lies and
    /// left there - not moved into assets\ first and then reported under a path that no longer exists.
    /// </summary>
    [Fact]
    public async Task A_hash_named_file_that_is_no_picture_stays_where_it_lies()
    {
        var campaign = Campaign("Sturmküste");
        var backup = Directory.CreateDirectory(Path.Combine(campaign, "Sicherung")).FullName;
        var name = new string('a', 64) + ".png";

        File.WriteAllBytes(Path.Combine(backup, name), FakeImageCodec.Unreadable);

        var (_, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal([Path.Combine("Sicherung", name)], report.Foreign);
        Assert.True(File.Exists(Path.Combine(backup, name)));
        Assert.False(File.Exists(Path.Combine(campaign, "assets", name)));
    }

    /// <summary>
    /// A copy of assets\ in a backup folder: a damaged copy never replaces the good file - nobody looked
    /// at whether those bytes match - and an intact one is a byte-identical duplicate, removed and
    /// counted as one rather than as a picture taken in.
    /// </summary>
    [Fact]
    public async Task A_hash_named_copy_never_replaces_the_stock_file_and_an_identical_one_is_a_duplicate()
    {
        var campaign = Campaign("Sturmküste");

        _codec.PassThrough = true;

        var taken = await Ingest(Library().Open("Sturmküste").Store, "ork");
        var name = taken.Asset.AssetId.Value + ".png";
        var stored = Path.Combine(campaign, "assets", name);
        var copy = Path.Combine(Directory.CreateDirectory(Path.Combine(campaign, "Sicherung")).FullName, name);

        File.WriteAllBytes(copy, Bytes("beschädigt"));

        var (_, damaged) = await OpenAndSettle("Sturmküste");

        Assert.Equal(Bytes("ork"), File.ReadAllBytes(stored));
        Assert.Equal([Path.Combine("Sicherung", name)], damaged.Foreign);
        Assert.Equal(0, damaged.Adopted);

        File.WriteAllBytes(copy, Bytes("ork"));

        var (_, intact) = await OpenAndSettle("Sturmküste");

        Assert.Equal(1, intact.Duplicates);
        Assert.Equal(0, intact.Adopted);
        Assert.False(File.Exists(copy));
        Assert.Equal(Bytes("ork"), File.ReadAllBytes(stored));
    }

    /// <summary>The stock file of an entry marked missing, found under its hash name elsewhere, goes back into place.</summary>
    [Fact]
    public async Task A_hash_named_copy_of_a_missing_file_heals_its_entry()
    {
        var campaign = Campaign("Sturmküste");

        _codec.PassThrough = true;

        var taken = await Ingest(Library().Open("Sturmküste").Store, "ork");
        var name = taken.Asset.AssetId.Value + ".png";
        var copy = Path.Combine(Directory.CreateDirectory(Path.Combine(campaign, "Sicherung")).FullName, name);

        File.Move(Path.Combine(campaign, "assets", name), copy);

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal(1, report.Adopted);
        Assert.Empty(opened.Store.Missing);
        Assert.True(File.Exists(Path.Combine(campaign, "assets", name)));
    }

    /// <summary>
    /// A hash name does not get a file past the intake either: one in a format the stock does not
    /// deliver is converted like any other (and its original stays), and one beyond the limits is not
    /// taken in at all.
    /// </summary>
    [Fact]
    public async Task A_hash_named_file_in_another_format_or_beyond_the_limits_does_not_bypass_the_intake()
    {
        var campaign = Campaign("Sturmküste");
        var webp = Path.Combine(campaign, Hash(Bytes("drache")) + ".webp");

        File.WriteAllBytes(webp, Bytes("drache"));

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal("png", Assert.Single(opened.Store.Entries).Extension);
        Assert.Equal(1, report.Adopted);
        Assert.True(File.Exists(webp));

        var huge = Path.Combine(campaign, "assets", Hash(Bytes("riese")) + ".png");

        File.WriteAllBytes(huge, Bytes("riese"));
        _codec.Claims = new ImageProbe("png", 30_000, 64, 1);

        var (reopened, beyond) = await OpenAndSettle("Sturmküste");

        Assert.Single(reopened.Store.Entries);
        Assert.Contains(Path.Combine("assets", Path.GetFileName(huge)), beyond.Foreign);
    }

    /// <summary>A file deleted by hand leaves its entry marked, not removed - and taking the same picture in again heals it.</summary>
    [Fact]
    public async Task A_missing_file_is_marked_and_heals_when_the_picture_comes_back()
    {
        Campaign("Sturmküste");
        var library = Library();
        var taken = await Ingest(library.Open("Sturmküste").Store, "ork");

        foreach (var file in Directory.GetFiles(Path.Combine(_base, "Sturmküste", "assets"), taken.Asset.AssetId.Value + ".*"))
        {
            File.Delete(file);
        }

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal(["Ork"], report.Missing);
        Assert.Single(opened.Store.Entries);

        var again = await Ingest(opened.Store, "ork", "Anderer Name");

        Assert.Equal(taken.Asset.AssetId, again.Asset.AssetId);
        Assert.Equal("Ork", again.Asset.Name);
        Assert.Empty(opened.Store.Missing);
    }

    /// <summary>The DM's notes stay untouched and are reported once; operating-system litter is not even mentioned.</summary>
    [Fact]
    public async Task Foreign_files_stay_and_litter_is_not_mentioned()
    {
        var campaign = Campaign("Sturmküste");

        File.WriteAllBytes(Path.Combine(campaign, "notizen.txt"), FakeImageCodec.Unreadable);
        File.WriteAllText(Path.Combine(campaign, "Thumbs.db"), "x");

        var (_, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal(["notizen.txt"], report.Foreign);
        Assert.True(File.Exists(Path.Combine(campaign, "notizen.txt")));
    }

    /// <summary>
    /// Rebuilt, not refused: an inventory that does not read gives way to one made from the files,
    /// with names from the scenes - and the unreadable file is set aside, not deleted.
    /// </summary>
    [Fact]
    public async Task An_unreadable_inventory_is_rebuilt_with_names_from_the_scenes()
    {
        Campaign("Sturmküste");
        var library = Library();
        var first = library.Open("Sturmküste");
        var taken = await Ingest(first.Store, "ork");

        first.Shelf.SaveScene("Dorf", new PixelSize(1920, 1080), SceneState.Empty with
        {
            Items = [Item(taken.Asset.AssetId, "Ork der Szene")],
        }, overwrite: false);

        File.WriteAllText(Path.Combine(_base, "Sturmküste", "inventory.json"), "{ kaputt");

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.True(report.Reconstructed);
        Assert.Equal("Ork der Szene", Assert.Single(opened.Store.Entries).Name);
        Assert.Equal(taken.Asset.AssetId.Value, opened.Store.Entries[0].AssetId);
        Assert.Single(Directory.GetFiles(Path.Combine(_base, "Sturmküste"), "inventory.json.unreadable-*"));
    }

    /// <summary>A stock without findings is surveyed by names alone - nothing to adopt, nothing missing, nothing to say.</summary>
    [Fact]
    public async Task A_folder_without_findings_reports_nothing()
    {
        Campaign("Sturmküste");
        await Ingest(Library().Open("Sturmküste").Store, "ork");

        var (_, report) = await OpenAndSettle("Sturmküste");

        Assert.True(report.IsEmpty);
    }

    [Fact]
    public async Task A_missing_thumbnail_is_made_again_without_a_word()
    {
        Campaign("Sturmküste");
        var taken = await Ingest(Library().Open("Sturmküste").Store, "ork");
        var opened = Library().Open("Sturmküste");
        var thumbnail = opened.Store.ThumbnailPath(taken.Asset.AssetId);

        File.Delete(thumbnail);

        var report = await Reconciliation.SettleAsync(opened.Store, opened.Shelf, opened.Findings, cancellationToken: Cancellation);

        Assert.True(File.Exists(thumbnail));
        Assert.True(report.IsEmpty);
    }

    private CampaignLibrary Library() =>
        new(_base, directory => AssetStore.Open(directory, _codec, _time), _time);

    private string Campaign(string name)
    {
        var path = Path.Combine(_base, name);

        Directory.CreateDirectory(path);

        return path;
    }

    private async Task<(OpenCampaign Campaign, CampaignReport Report)> OpenAndSettle(string name)
    {
        var opened = Library().Open(name);
        var report = await Reconciliation.SettleAsync(opened.Store, opened.Shelf, opened.Findings, cancellationToken: Cancellation);

        return (opened, report);
    }

    private static byte[] Bytes(string seed) => Encoding.UTF8.GetBytes("source:" + seed);

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    private static async Task<IngestResult.Taken> Ingest(AssetStore store, string seed, string? name = null) =>
        Assert.IsType<IngestResult.Taken>(await store.IngestAsync(
            Bytes(seed), name ?? char.ToUpperInvariant(seed[0]) + seed[1..], Cancellation));

    private static ImageItem Item(AssetId asset, string name) =>
        new(
            new ItemId(Guid.NewGuid()), 0.5, 0.5, 0.4, 1, 0, 1, false, false, 1,
            asset,
            new AssetMeta(64, 64, "png", 10, false, new string('d', 64)),
            name,
            false,
            false);
}
