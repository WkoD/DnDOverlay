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

    /// <summary>
    /// Part 3: a campaigns folder that cannot be reached - the stick pulled - is said so, and nothing is
    /// created: the first picture is refused with the folder named, not taken in somewhere new.
    /// </summary>
    [Fact]
    public async Task An_unreachable_campaigns_folder_creates_nothing_and_refuses_the_first_picture()
    {
        var gone = Path.Combine(_base, "Stick", "Kampagnen");
        var library = new CampaignLibrary(gone, directory => AssetStore.Open(directory, _codec, _time), _time);
        var stock = new OpenStock(() => library.CreateUnnamed().Store);

        Assert.False(library.Reachable);
        Assert.Throws<CampaignBaseUnreachableException>(library.CreateUnnamed);

        var refused = Assert.IsType<IngestResult.Refused>(await stock.IngestAsync(Bytes("ork"), "Ork", Cancellation));

        Assert.Contains(gone, refused.Detail, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_base));
    }

    /// <summary>
    /// A place the DM chose existed when it was chosen: gone, it is unreachable although its drive is
    /// there. The default place comes into being with the first picture.
    /// </summary>
    [Fact]
    public void A_chosen_place_must_exist_and_the_default_one_may_come_into_being()
    {
        Directory.CreateDirectory(_base);

        var place = Path.Combine(_base, "Kampagnen");

        Assert.True(new CampaignLibrary(place, directory => AssetStore.Open(directory, _codec, _time), _time).Reachable);
        Assert.False(new CampaignLibrary(place, directory => AssetStore.Open(directory, _codec, _time), _time, baseIsDefault: false).Reachable);

        var library = Library();
        var moved = Path.Combine(_base, "Verlegt");

        library.Relocate(moved);
        Assert.False(library.Reachable);

        Directory.CreateDirectory(moved);
        Assert.True(library.Reachable);
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

    /// <summary>
    /// An ingest that finishes after the rename - it was still decoding while the folder moved - writes
    /// nothing into the old path: no "~unbenannt" with a full inventory and one file in it comes back
    /// in the switcher. The picture is refused with a reason instead.
    /// </summary>
    [Fact]
    public async Task A_picture_finished_after_a_rename_does_not_bring_the_old_folder_back()
    {
        var library = Library();
        var campaign = library.CreateUnnamed();
        var old = campaign.Store;

        await Ingest(old, "eins");
        Assert.Equal(CampaignOutcome.Done, library.Rename(campaign, "Kerkerwelt").Outcome);

        var late = await old.IngestAsync(Bytes("zwei"), "Zwei", Cancellation);

        Assert.Equal(IntakeRejection.Unavailable, Assert.IsType<IngestResult.Refused>(late).Reason);
        Assert.False(Directory.Exists(Path.Combine(_base, "~unbenannt")));
        Assert.Equal(["Kerkerwelt"], library.List().Select(summary => summary.Name));
    }

    /// <summary>The same after a delete: the campaign stays deleted (Part 3), and so does a closed one's stock.</summary>
    [Fact]
    public async Task Nothing_written_after_a_delete_brings_the_campaign_back()
    {
        var library = Library();
        var campaign = library.CreateUnnamed();

        library.Delete(campaign);

        Assert.IsType<IngestResult.Refused>(await campaign.Store.IngestAsync(Bytes("eins"), "Eins", Cancellation));
        Assert.False(campaign.Store.Remove(new AssetId(new string('a', 64))));
        Assert.Empty(library.List());
        Assert.False(Directory.Exists(campaign.Directory));
    }

    /// <summary>
    /// A folder check still settling when the campaign is closed stops there: it moves, takes in and
    /// deletes nothing more, and says it was interrupted, so its report is not shown for a campaign
    /// that is no longer open.
    /// </summary>
    [Fact]
    public async Task A_folder_check_stops_when_its_campaign_is_closed()
    {
        var campaign = Campaign("Sturmküste");

        _codec.PassThrough = true;
        File.WriteAllBytes(Path.Combine(campaign, "Ork.png"), Bytes("ork"));

        var opened = Library().Open("Sturmküste");

        opened.Close();

        var report = await Reconciliation.SettleAsync(opened.Store, opened.Shelf, opened.Findings, cancellationToken: Cancellation);

        Assert.True(report.Interrupted);
        Assert.Equal(0, report.Adopted);
        Assert.True(File.Exists(Path.Combine(campaign, "Ork.png")));
        Assert.Empty(opened.Store.Entries);
    }

    /// <summary>A rename the system refuses leaves the campaign as it was - and its stock taking pictures again.</summary>
    [Fact]
    public async Task A_refused_rename_leaves_the_stock_open()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows refuses to move a folder with a file held open inside");
        }

        var library = Library();
        var campaign = library.CreateUnnamed();

        using (File.Open(Path.Combine(campaign.Directory, "inventory.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(CampaignOutcome.Busy, library.Rename(campaign, "Kerkerwelt").Outcome);
        }

        Assert.False(campaign.Store.IsClosed);
        await Ingest(campaign.Store, "eins");
    }

    /// <summary>
    /// The folder moved, but the campaign could not be read at its new place right now: it goes back
    /// under its old name and the rename answers "busy", instead of an exception with the folder moved
    /// and the open campaign still pointing at the old path.
    /// </summary>
    [Fact]
    public async Task A_rename_whose_new_place_cannot_be_read_puts_the_folder_back()
    {
        var refuse = false;
        var library = new CampaignLibrary(
            _base,
            directory => refuse ? throw new IOException("held by a sync client") : AssetStore.Open(directory, _codec, _time),
            _time);
        var campaign = library.CreateUnnamed();

        refuse = true;

        var (outcome, after) = library.Rename(campaign, "Kerkerwelt");

        Assert.Equal(CampaignOutcome.Busy, outcome);
        Assert.Same(campaign, after);
        Assert.True(Directory.Exists(Path.Combine(_base, "~unbenannt")));
        Assert.False(Directory.Exists(Path.Combine(_base, "Kerkerwelt")));
        Assert.False(campaign.Store.IsClosed);
        await Ingest(campaign.Store, "eins");
    }

    /// <summary>
    /// A rename by case only whose second step is refused takes the first back: the folder does not stay
    /// under "<c>.renaming</c>", listed as a campaign of its own. Staged with a folder of the target
    /// spelling beside it, which only a case-sensitive file system allows.
    /// </summary>
    [Fact]
    public void A_case_only_rename_whose_second_step_fails_leaves_the_folder_where_it_was()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("needs a case-sensitive file system to stage the refused second step");
        }

        var library = Library();
        var campaign = library.Open(Path.GetFileName(Campaign("Kerkerwelt")));

        Directory.CreateDirectory(Path.Combine(_base, "kerkerwelt", "besetzt"));

        Assert.Equal(CampaignOutcome.Busy, library.Rename(campaign, "kerkerwelt").Outcome);
        Assert.True(Directory.Exists(Path.Combine(_base, "Kerkerwelt")));
        Assert.False(Directory.Exists(Path.Combine(_base, "Kerkerwelt.renaming")));
    }

    /// <summary>
    /// A hand-edited unnamed inventory with "entries": null does not keep the switcher from opening,
    /// nor the stock from reading; an entry without an identity is no entry.
    /// </summary>
    [Fact]
    public void An_inventory_with_null_entries_still_lists_and_opens()
    {
        var library = Library();
        var campaign = library.CreateUnnamed();
        var inventory = Path.Combine(campaign.Directory, "inventory.json");

        File.WriteAllText(inventory, "{ \"schemaVersion\": 1, \"createdAt\": \"2026-10-01T00:00:00Z\", \"entries\": null }");

        Assert.Null(Assert.Single(library.List()).Pictures);
        Assert.Empty(library.Open(campaign.Name).Store.Entries);

        File.WriteAllText(inventory, "{ \"schemaVersion\": 1, \"createdAt\": \"2026-10-01T00:00:00Z\", \"entries\": [ null, { \"name\": \"ohne Kennung\" } ] }");

        Assert.Empty(library.Open(campaign.Name).Store.Entries);
    }

    /// <summary>
    /// An inventory held by a sync client: opening is refused with one stated kind of reason the caller
    /// can show - not with whichever I/O exception the file system chose, which the switch did not catch.
    /// </summary>
    [Fact]
    public void A_held_inventory_refuses_the_open_with_a_stated_reason()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows refuses to read a file held without sharing");
        }

        var library = Library();
        var campaign = library.CreateUnnamed();

        using (File.Open(Path.Combine(campaign.Directory, "inventory.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var refused = Assert.Throws<CampaignUnavailableException>(() => library.Open(campaign.Name));

            Assert.Contains(campaign.Name, refused.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Deleting_takes_the_folder()
    {
        var library = Library();
        var campaign = library.CreateUnnamed();

        Assert.Equal(CampaignOutcome.Done, library.Delete(campaign));
        Assert.Empty(library.List());
    }

    /// <summary>
    /// A file held open inside - a display still downloading a picture - and the delete deletes nothing
    /// and says so, instead of leaving half a folder behind and throwing.
    /// </summary>
    [Fact]
    public async Task A_delete_while_a_file_is_held_open_deletes_nothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows refuses to move a folder with a file held open inside");
        }

        var library = Library();
        var campaign = library.CreateUnnamed();
        var taken = await Ingest(campaign.Store, "eins");
        var picture = Assert.Single(Directory.GetFiles(Path.Combine(campaign.Directory, "assets"), taken.Asset.AssetId.Value + ".*"));

        using (File.Open(picture, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(CampaignOutcome.Busy, library.Delete(campaign));
        }

        Assert.True(File.Exists(picture));
        Assert.True(File.Exists(Path.Combine(campaign.Directory, "inventory.json")));
        Assert.Single(library.List());
        Assert.False(campaign.Store.IsClosed);
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
    /// taken in, and its original is KEPT: what lies in assets\ is not that file, and Part 3 removes
    /// byte-identical copies only. It is moved into originals\ and named once (DM decision of
    /// 22.10.2026); the next opening does not meet it again.
    /// </summary>
    [Fact]
    public async Task A_picture_converted_on_the_way_in_has_its_original_moved_into_originals_and_named_once()
    {
        var campaign = Campaign("Sturmküste");
        var original = Path.Combine(campaign, "Drache.webp");
        var kept = Path.Combine(campaign, "originals", "Drache.webp");

        File.WriteAllBytes(original, Bytes("drache"));

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal(1, report.Adopted);
        Assert.Equal([Path.Combine("originals", "Drache.webp")], report.Originals);
        Assert.Empty(report.Foreign);
        Assert.Equal("Drache", Assert.Single(opened.Store.Entries).Name);
        Assert.False(File.Exists(original));
        Assert.Equal(Bytes("drache"), File.ReadAllBytes(kept));

        var (reopened, again) = await OpenAndSettle("Sturmküste");

        Assert.True(again.IsEmpty);
        Assert.Single(reopened.Store.Entries);
        Assert.Equal(Bytes("drache"), File.ReadAllBytes(kept));
    }

    /// <summary>
    /// originals\ never loses a file: an original whose name is taken there - the same token laid in
    /// twice, a picture of that name kept earlier - is numbered, and what lay there stays as it was. A
    /// picture the DM puts into originals\ himself is not taken in: the check passes over the folder.
    /// </summary>
    [Fact]
    public async Task An_original_whose_name_is_taken_in_originals_is_numbered_and_overwrites_nothing()
    {
        var campaign = Campaign("Sturmküste");
        var originals = Directory.CreateDirectory(Path.Combine(campaign, "originals")).FullName;

        File.WriteAllBytes(Path.Combine(originals, "Drache.webp"), Bytes("früher"));
        File.WriteAllBytes(Path.Combine(originals, "Drache (2).webp"), Bytes("auch früher"));
        File.WriteAllBytes(Path.Combine(campaign, "Drache.webp"), Bytes("drache"));

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal([Path.Combine("originals", "Drache (3).webp")], report.Originals);
        Assert.Equal(Bytes("früher"), File.ReadAllBytes(Path.Combine(originals, "Drache.webp")));
        Assert.Equal(Bytes("auch früher"), File.ReadAllBytes(Path.Combine(originals, "Drache (2).webp")));
        Assert.Equal(Bytes("drache"), File.ReadAllBytes(Path.Combine(originals, "Drache (3).webp")));
        Assert.Equal("Drache", Assert.Single(opened.Store.Entries).Name);
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
    /// deliver is converted like any other (and its original is kept in originals\), and one beyond the
    /// limits is not taken in at all.
    /// </summary>
    [Fact]
    public async Task A_hash_named_file_in_another_format_or_beyond_the_limits_does_not_bypass_the_intake()
    {
        var campaign = Campaign("Sturmküste");
        var name = Hash(Bytes("drache")) + ".webp";
        var webp = Path.Combine(campaign, name);

        File.WriteAllBytes(webp, Bytes("drache"));

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Equal("png", Assert.Single(opened.Store.Entries).Extension);
        Assert.Equal(1, report.Adopted);
        Assert.True(File.Exists(Path.Combine(campaign, "originals", name)));

        var huge = Path.Combine(campaign, "assets", Hash(Bytes("riese")) + ".png");

        File.WriteAllBytes(huge, Bytes("riese"));
        _codec.Claims = new ImageProbe("png", 30_000, 64, 1);

        var (reopened, beyond) = await OpenAndSettle("Sturmküste");

        Assert.Single(reopened.Store.Entries);
        Assert.Contains(Path.Combine("assets", Path.GetFileName(huge)), beyond.Foreign);
    }

    /// <summary>
    /// M5b5: a picture file that cannot be deleted right now - a display still downloading it - stays,
    /// the entry goes, and the next folder check does not take it back into the stock under its hash.
    /// While it is still held it is named as foreign; once free, it is deleted without a word - the
    /// delete the DM asked for, finished late (DM decision of 22.10.2026). Taken in again on purpose, it
    /// is a picture like any other.
    /// </summary>
    [Fact]
    public async Task A_removed_picture_whose_file_was_held_open_is_deleted_quietly_once_free_and_named_foreign_while_held()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows refuses to delete a file that somebody holds open");
        }

        var campaign = Campaign("Sturmküste");
        var store = Library().Open("Sturmküste").Store;
        var taken = await Ingest(store, "ork");
        var file = Assert.Single(Directory.GetFiles(Path.Combine(campaign, "assets"), taken.Asset.AssetId.Value + ".*"));

        CampaignReport held;

        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(store.Remove(taken.Asset.AssetId));
            Assert.True(File.Exists(file));

            (var opened, held) = await OpenAndSettle("Sturmküste");

            Assert.Empty(opened.Store.Entries);
        }

        Assert.Equal(0, held.Adopted);
        Assert.Equal([Path.Combine("assets", Path.GetFileName(file))], held.Foreign);
        Assert.True(File.Exists(file));

        var (free, report) = await OpenAndSettle("Sturmküste");

        Assert.True(report.IsEmpty);
        Assert.False(File.Exists(file));
        Assert.Empty(free.Store.Entries);
        Assert.DoesNotContain(Path.GetFileName(file), File.ReadAllText(Path.Combine(campaign, "inventory.json")), StringComparison.OrdinalIgnoreCase);

        await Ingest(free.Store, "ork");

        var (_, after) = await OpenAndSettle("Sturmküste");

        Assert.True(after.IsEmpty);
        Assert.True(File.Exists(file));
    }

    /// <summary>
    /// Only the removed picture's own file in assets\ is deleted late: a copy of it in a backup folder is
    /// the DM's, stays, and is named as foreign as before.
    /// </summary>
    [Fact]
    public async Task A_copy_of_a_removed_picture_elsewhere_in_the_folder_is_not_deleted()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows refuses to delete a file that somebody holds open");
        }

        var campaign = Campaign("Sturmküste");
        var store = Library().Open("Sturmküste").Store;
        var taken = await Ingest(store, "ork");
        var file = Assert.Single(Directory.GetFiles(Path.Combine(campaign, "assets"), taken.Asset.AssetId.Value + ".*"));
        var copy = Path.Combine(Directory.CreateDirectory(Path.Combine(campaign, "Sicherung")).FullName, Path.GetFileName(file));

        File.Copy(file, copy);

        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(store.Remove(taken.Asset.AssetId));
        }

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(copy));
        Assert.Equal([Path.Combine("Sicherung", Path.GetFileName(file))], report.Foreign);
        Assert.Empty(opened.Store.Entries);
    }

    /// <summary>
    /// One file that cannot be handled ends nothing: an orphan gone since the first look - the case of
    /// "check folder again" while the opening's check works through the same files - is named in the
    /// report, and the files after it are still taken in.
    /// </summary>
    [Fact]
    public async Task A_file_gone_since_the_first_look_is_named_and_the_rest_still_comes_in()
    {
        var campaign = Campaign("Sturmküste");

        File.WriteAllBytes(Path.Combine(campaign, "A Weg.png"), Bytes("weg"));
        File.WriteAllBytes(Path.Combine(campaign, "B Ork.png"), Bytes("ork"));

        var opened = Library().Open("Sturmküste");

        File.Delete(Path.Combine(campaign, "A Weg.png"));

        var report = await Reconciliation.SettleAsync(opened.Store, opened.Shelf, opened.Findings, cancellationToken: Cancellation);

        Assert.Equal(["A Weg.png"], report.Skipped);
        Assert.Equal(1, report.Adopted);
        Assert.Equal("B Ork", Assert.Single(opened.Store.Entries).Name);
    }

    /// <summary>
    /// A locked file is named and left for the next check; a read-only copy laid in from a share is taken
    /// in, and because it will not be deleted it stays and is named with the skipped ones - on every
    /// opening, without an exception on any of them.
    /// </summary>
    [Fact]
    public async Task A_locked_file_and_a_read_only_copy_are_skipped()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows refuses to read a file held without sharing, or to delete a read-only one");
        }

        var campaign = Campaign("Sturmküste");
        var locked = Path.Combine(campaign, "Gesperrt.png");
        var readOnly = Path.Combine(campaign, "Schreibgeschützt.png");

        _codec.PassThrough = true;

        File.WriteAllBytes(locked, Bytes("gesperrt"));
        File.WriteAllBytes(readOnly, Bytes("nur lesen"));
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        try
        {
            CampaignReport report;

            using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                (_, report) = await OpenAndSettle("Sturmküste");
            }

            Assert.Equal(["Gesperrt.png", "Schreibgeschützt.png"], report.Skipped.Order(StringComparer.Ordinal));
            Assert.Empty(report.Originals);
            Assert.Equal(1, report.Adopted);

            var (_, again) = await OpenAndSettle("Sturmküste");

            Assert.Equal(["Schreibgeschützt.png"], again.Skipped);
            Assert.True(File.Exists(readOnly));
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// Scratch files a crash left between writing and renaming are ours and derived: removed without a
    /// word - not taken in as a half picture from their partial bytes, not reported as foreign on every
    /// opening. A DM's own .tmp elsewhere is none of ours.
    /// </summary>
    [Fact]
    public async Task Scratch_files_left_by_a_crash_are_removed_without_a_word()
    {
        var campaign = Campaign("Sturmküste");
        var hash = new string('a', 64);
        var picture = Path.Combine(campaign, "assets", hash + ".png.12" + AtomicFile.TemporarySuffix);
        var thumbnail = Path.Combine(campaign, "assets", "thumbs", hash + "_256.png" + AtomicFile.TemporarySuffix);
        var inventory = Path.Combine(campaign, "inventory.json.7" + AtomicFile.TemporarySuffix);
        var theirs = Path.Combine(campaign, "Notizen.tmp");

        Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
        File.WriteAllBytes(picture, Bytes("halb"));
        File.WriteAllBytes(thumbnail, Bytes("halb"));
        File.WriteAllText(inventory, "{");
        File.WriteAllBytes(theirs, FakeImageCodec.Unreadable);

        var (opened, report) = await OpenAndSettle("Sturmküste");

        Assert.Empty(opened.Store.Entries);
        Assert.Equal(["Notizen.tmp"], report.Foreign);
        Assert.False(File.Exists(picture));
        Assert.False(File.Exists(thumbnail));
        Assert.False(File.Exists(inventory));
        Assert.True(File.Exists(theirs));
    }

    /// <summary>The campaign folder removed by hand while open: "check folder again" reports every picture missing instead of throwing.</summary>
    [Fact]
    public async Task A_folder_removed_while_open_is_checked_without_an_exception()
    {
        var campaign = Campaign("Sturmküste");
        var opened = Library().Open("Sturmküste");

        await Ingest(opened.Store, "ork");
        Directory.Delete(campaign, recursive: true);

        var report = await Reconciliation.SettleAsync(opened.Store, opened.Shelf, Reconciliation.Survey(opened.Store), cancellationToken: Cancellation);

        Assert.Equal(["Ork"], report.Missing);
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
