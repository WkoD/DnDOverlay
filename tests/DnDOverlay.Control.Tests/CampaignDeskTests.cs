using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;
using DnDOverlay.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The ways from one campaign to the next (Part 3) where they meet a running intake, a held file or
/// a folder check: refused with a reason, never a crash, never half done.
/// </summary>
public sealed class CampaignDeskTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>While pictures come in, a switch would split the run over two campaigns: refused, with the reason.</summary>
    [Fact]
    public async Task A_switch_while_an_intake_runs_is_refused()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);

        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));
        bench.Campaign("Tyranny");
        bench.Desk.Taking = true;

        Assert.Equal(Strings.CampaignIntakeRunning, await bench.Desk.SwitchAsync("Tyranny"));
        Assert.Equal("Vandalia", bench.Desk.Open?.Name);
    }

    /// <summary>
    /// A delete in the middle of an intake would have the next picture create an unnamed campaign
    /// right after the DM deleted his: refused, the folder untouched. A rename would move the folder
    /// between two writes: refused as busy.
    /// </summary>
    [Fact]
    public async Task Delete_and_rename_while_an_intake_runs_are_refused()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);

        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));
        bench.Desk.Taking = true;

        Assert.Equal(Strings.CampaignIntakeRunning, await bench.Desk.DeleteAsync());
        Assert.Equal(CampaignOutcome.Busy, bench.Desk.Rename("Vinland"));

        // Closing for the move of the campaign folder: nobody moves the folder an intake writes into.
        Assert.Equal(Strings.CampaignIntakeRunning, await bench.Desk.CloseAsync());
        Assert.True(Directory.Exists(Path.Combine(bench.Base, "Vandalia")));
        Assert.Equal("Vandalia", bench.Desk.Open?.Name);
    }

    /// <summary>
    /// A file of the campaign still open elsewhere (a display downloading a map): the delete is refused
    /// before anything goes, the table included - not half the folder deleted and the control ended
    /// by the exception.
    /// </summary>
    [Fact]
    public async Task A_delete_with_a_file_held_open_deletes_nothing_and_says_why()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);

        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));
        await bench.Session.AddItemAsync(CampaignBench.Screen, await bench.PictureAsync("Karte", 1), position: null, Cancellation);

        var open = bench.Desk.Open!;
        var file = Directory.EnumerateFiles(Path.Combine(open.Directory, "assets")).First();
        string? refused;

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refused = await bench.Desk.DeleteAsync();
        }

        Assert.Equal(Strings.CampaignDeleteHeld("Vandalia"), refused);
        Assert.Same(open, bench.Desk.Open);
        Assert.False(open.Store.IsClosed);
        Assert.Single((await bench.Session.GetSceneAsync(CampaignBench.Screen, Cancellation)).Items);
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(open.Directory, "inventory.json")));

        Assert.Null(await bench.Desk.DeleteAsync());
        Assert.False(Directory.Exists(open.Directory));
        Assert.Null(bench.Desk.Open);
    }

    /// <summary>
    /// "Check folder again" while Explorer still copies a file in: the file cannot be read yet. Named in
    /// the report beside the stage as one to try again - not an exception out of the tidy-up view that
    /// ends the control, and not a check that stops at it.
    /// </summary>
    [Fact]
    public async Task Checking_the_folder_with_a_file_still_being_copied_reports_instead_of_crashing()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);
        var reports = new ConcurrentQueue<string>();

        bench.Desk.Reported += (_, report) => reports.Enqueue(report.Line);
        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

        var copying = Path.Combine(bench.Desk.Open!.Directory, "neu.png");

        await File.WriteAllBytesAsync(copying, CampaignBench.Png(7), Cancellation);

        using (new FileStream(copying, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await bench.Desk.RescanAsync();
        }

        Assert.Contains(Strings.DeskSkipped(1, "neu.png"), reports.Last(), StringComparison.Ordinal);
        Assert.True(File.Exists(copying));
    }

    /// <summary>
    /// Behind the report's line, the names it counts - a missing entry with its picture, so the box can
    /// jump to it in the stock or remove it; a foreign file by its place in the folder, for Explorer
    /// (Part 3, phase 3).
    /// </summary>
    [Fact]
    public async Task The_report_carries_the_names_behind_its_line()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);
        var reports = new ConcurrentQueue<FolderReport>();

        bench.Desk.Reported += (_, report) => reports.Enqueue(report);
        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

        var karte = await bench.PictureAsync("Karte", 1);
        var directory = bench.Desk.Open!.Directory;

        File.Delete(Directory.EnumerateFiles(Path.Combine(directory, "assets"), karte.AssetId.Value + ".*").Single());
        await File.WriteAllTextAsync(Path.Combine(directory, "notizen.txt"), "Kein Bild.", Cancellation);
        await bench.Desk.RescanAsync();

        var last = reports.Last();

        Assert.Same(bench.Desk.Open, last.Campaign);
        Assert.Equal([new MissingPicture(karte.AssetId, "Karte")], last.Missing);
        Assert.Equal(["notizen.txt"], last.Foreign);
        Assert.True(last.HasNames);
        Assert.Contains(Strings.DeskMissing(1, "Karte"), last.Line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A campaign whose inventory another program holds while it opens: refused with the library's
    /// reason, nothing open - not an exception out of the switcher.
    /// </summary>
    [Fact]
    public async Task A_campaign_that_cannot_be_read_right_now_is_refused_with_its_reason()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);

        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));
        await bench.PictureAsync("Karte", 1);
        await bench.Desk.CloseAsync();

        string? refused;

        using (new FileStream(Path.Combine(bench.Base, "Vandalia", "inventory.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            refused = await bench.Desk.SwitchAsync("Vandalia");
        }

        Assert.NotNull(refused);
        Assert.Contains("Vandalia", refused, StringComparison.Ordinal);
        Assert.Null(bench.Desk.Open);
    }

    /// <summary>
    /// The campaign left behind takes no more writes - by a switch and by closing (for the move of the
    /// campaign folder): an ingest still decoding would otherwise write into its folder afterwards.
    /// </summary>
    [Fact]
    public async Task The_campaign_left_behind_is_closed()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);

        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

        var vandalia = bench.Desk.Open!;

        await bench.Desk.SwitchAsync(bench.Campaign("Tyranny"));

        var tyranny = bench.Desk.Open!;

        Assert.True(vandalia.Store.IsClosed);
        Assert.False(tyranny.Store.IsClosed);

        await bench.Desk.CloseAsync();

        Assert.True(tyranny.Store.IsClosed);
    }

    /// <summary>
    /// A check broken off because the campaign's stock closed under it says nothing beside the stage:
    /// half a report about a stock no longer in use would read as the whole one.
    /// </summary>
    [Fact]
    public async Task A_check_broken_off_by_a_closed_stock_reports_nothing()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);
        var reports = new ConcurrentQueue<string>();

        bench.Desk.Reported += (_, report) => reports.Enqueue(report.Line);
        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));
        await bench.Desk.RescanAsync();

        var before = reports.Count;

        await File.WriteAllBytesAsync(Path.Combine(bench.Desk.Open!.Directory, "neu.png"), CampaignBench.Png(7), Cancellation);
        bench.Desk.Open.Close();
        await bench.Desk.RescanAsync();

        Assert.Equal(before, reports.Count);
    }

    /// <summary>
    /// A campaign switched away while its folder check ran: its report does not appear beside the
    /// stage under the campaign open now - only the open one's does.
    /// </summary>
    [Fact]
    public async Task The_report_of_a_campaign_switched_away_does_not_appear()
    {
        // The desk's dispatcher does not run until both campaigns have opened: the first check's report
        // is queued while the first campaign is open and delivered when the second one is.
        using var thread = new DispatcherThread(held: true);
        using var bench = new CampaignBench(thread.Dispatcher);
        var reports = new ConcurrentQueue<string>();
        var second = new TaskCompletionSource();

        bench.Desk.Reported += (_, report) =>
        {
            reports.Enqueue(report.Line);

            if (report.Line.Contains("Tyranny", StringComparison.Ordinal))
            {
                second.TrySetResult();
            }
        };

        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));
        bench.Campaign("Tyranny");
        await bench.Desk.SwitchAsync("Tyranny");

        thread.Release();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        await thread.Dispatcher.InvokeAsync(() => { }).Task.WaitAsync(Cancellation);

        Assert.DoesNotContain(reports, words => words.Contains("Vandalia", StringComparison.Ordinal));
    }

    /// <summary>
    /// "Open folder" on a campaigns folder that cannot be reached - a chosen place whose drive is
    /// gone - creates nothing there: a new, empty folder would look like campaigns lost (Part 3). The
    /// default place comes into being as with the first picture.
    /// </summary>
    [Fact]
    public void An_unreachable_campaigns_folder_is_not_created()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);
        var fresh = Path.Combine(bench.Base, "neu");

        Assert.True(bench.Desk.Reachable);

        var library = new CampaignLibrary(fresh, directory => throw new InvalidOperationException(directory), TimeProvider.System);
        var desk = new CampaignDesk(library, bench.Stock, bench.Session, thread.Dispatcher, NullLogger.Instance);

        Assert.True(desk.EnsureBaseFolder());
        Assert.True(Directory.Exists(fresh));

        var gone = Path.Combine(bench.Base, "abgezogen", "Kampagnen");

        desk.Relocate(gone);

        Assert.False(desk.Reachable);
        Assert.False(desk.EnsureBaseFolder());
        Assert.False(Directory.Exists(gone));
    }

    /// <summary>The end of an intake changes the stock as much as a switch: the tidy-up view and "Set up campaign" draw again.</summary>
    [Fact]
    public void The_end_of_an_intake_announces_a_changed_stock()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);
        var changed = 0;

        bench.Desk.StockChanged += (_, _) => changed++;
        bench.Desk.Taking = true;
        bench.Desk.Taking = false;
        bench.Desk.StockEdited();

        Assert.Equal(2, changed);
    }

    /// <summary>A write the disk refuses becomes its reason - the sentence the status line gets instead of a crash.</summary>
    [Fact]
    public void A_refused_write_becomes_its_reason()
    {
        Assert.False(CampaignDesk.Writes<int>(() => throw new IOException("Datenträger voll"), out _, out var refused));
        Assert.Equal("Datenträger voll", refused);
        Assert.True(CampaignDesk.Writes(() => 3, out var result, out _));
        Assert.Equal(3, result);
    }

    /// <summary>
    /// The shelf states the disk's refusal as Failed instead of throwing: that is a reason to say too,
    /// not an outcome to pass over as if nothing had been asked. Taken stays the caller's question.
    /// </summary>
    [Fact]
    public void A_shelf_that_failed_becomes_a_reason()
    {
        Assert.False(CampaignDesk.Shelves(() => ShelfOutcome.Failed, out _, out var refused));
        Assert.Equal(Strings.ShelfRefused, refused);
        Assert.False(CampaignDesk.Shelves(() => throw new IOException("Datenträger voll"), out _, out refused));
        Assert.Equal("Datenträger voll", refused);
        Assert.True(CampaignDesk.Shelves(() => ShelfOutcome.Taken, out var outcome, out _));
        Assert.Equal(ShelfOutcome.Taken, outcome);
    }

    /// <summary>A shelf delete answering false while the entry is still listed was refused; not listed, there was nothing to delete.</summary>
    [Fact]
    public void A_shelf_delete_that_left_its_entry_becomes_a_reason()
    {
        Assert.False(CampaignDesk.Removes(() => false, () => true, out var refused));
        Assert.Equal(Strings.ShelfRefused, refused);
        Assert.True(CampaignDesk.Removes(() => false, () => false, out _));
        Assert.True(CampaignDesk.Removes(() => true, () => true, out _));
    }

    /// <summary>
    /// End to end on a real shelf: a scene whose file another program holds is not deleted, stays
    /// listed - and the helper turns that into the stated reason the panels show.
    /// </summary>
    [Fact]
    public async Task A_scene_whose_file_is_held_is_not_deleted_and_says_why()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);

        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

        var shelf = bench.Desk.Open!.Shelf;

        Assert.Equal(ShelfOutcome.Done, shelf.SaveScene("Taverne", new PixelSize(1920, 1080), SceneState.Empty, overwrite: false));

        bool deleted;
        string? refused;

        using (new FileStream(Path.Combine(bench.Desk.Open.Directory, "scenes", "Taverne.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            deleted = CampaignDesk.Removes(() => shelf.DeleteScene("Taverne"), () => shelf.Scene("Taverne") is not null, out refused);
        }

        Assert.False(deleted);
        Assert.Equal(Strings.ShelfRefused, refused);
        Assert.NotNull(shelf.Scene("Taverne"));
    }
}
