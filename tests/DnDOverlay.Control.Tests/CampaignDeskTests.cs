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

        Assert.NotNull(refused);
        Assert.StartsWith(Strings.CampaignDeleteHeld("Vandalia", string.Empty)[..12], refused, StringComparison.Ordinal);
        Assert.Same(open, bench.Desk.Open);
        Assert.Single((await bench.Session.GetSceneAsync(CampaignBench.Screen, Cancellation)).Items);
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(open.Directory, "inventory.json")));

        Assert.Null(await bench.Desk.DeleteAsync());
        Assert.False(Directory.Exists(open.Directory));
        Assert.Null(bench.Desk.Open);
    }

    /// <summary>
    /// "Check folder again" while Explorer still copies a file in: the file cannot be read yet. Said in
    /// the report beside the stage - not an exception out of the tidy-up view that ends the control.
    /// </summary>
    [Fact]
    public async Task Checking_the_folder_with_a_file_still_being_copied_reports_instead_of_crashing()
    {
        using var thread = new DispatcherThread();
        using var bench = new CampaignBench(thread.Dispatcher);
        var reports = new ConcurrentQueue<string>();

        bench.Desk.Reported += (_, words) => reports.Enqueue(words);
        await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

        var copying = Path.Combine(bench.Desk.Open!.Directory, "neu.png");

        await File.WriteAllBytesAsync(copying, CampaignBench.Png(7), Cancellation);

        using (new FileStream(copying, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await bench.Desk.RescanAsync();
        }

        Assert.Contains(Strings.CampaignCheckFailed("Vandalia", string.Empty)[..20], reports.Last(), StringComparison.Ordinal);
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

        bench.Desk.Reported += (_, words) =>
        {
            reports.Enqueue(words);

            if (words.Contains("Tyranny", StringComparison.Ordinal))
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
}
