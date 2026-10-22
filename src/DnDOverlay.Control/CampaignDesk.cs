using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows.Threading;
using DnDOverlay.Campaign;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// Which campaign is open, and every way from one to the next (Part 3): open, switch, the unnamed
/// one the first picture makes, rename, delete. One owner, because the stock behind the hub, the
/// pictures on the stage and the scenes tab all have to change together.
/// <para>
/// <b>The control starts without a campaign.</b> A program that opens a stock by itself makes a
/// decision nobody made - who runs two groups would find the other group's campaign in front of
/// him and work in it a while before noticing. The empty start costs one grip and makes the choice
/// visible.
/// </para>
/// </summary>
internal sealed class CampaignDesk
{
    private readonly CampaignLibrary _library;
    private readonly ISessionApi _session;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    /// <summary>
    /// The folder check running or last run - the opening's or a "Check folder again". The next one
    /// waits for it: two reconciliations over one folder would move, delete and read the same
    /// orphan, and one of them would find it gone.
    /// </summary>
    private Task _checking = Task.CompletedTask;

    private bool _taking;

    /// <param name="stock">
    /// Built before this, because the hub needs its source before there is a session - and handed
    /// <see cref="EnsureOpen"/> through a closure, so the first picture reaches back here.
    /// </param>
    internal CampaignDesk(CampaignLibrary library, OpenStock stock, ISessionApi session, Dispatcher dispatcher, ILogger logger)
    {
        _library = library;
        _session = session;
        _dispatcher = dispatcher;
        _logger = logger;

        Stock = stock;
    }

    /// <summary>The stock of the open campaign, as the hub and the entrances see it.</summary>
    internal OpenStock Stock { get; }

    /// <summary>The open campaign, or <see langword="null"/>.</summary>
    internal OpenCampaign? Open { get; private set; }

    /// <summary>A different campaign is open, or none. Raised on the UI thread.</summary>
    internal event EventHandler? Changed;

    /// <summary>The reconciliation of a just-opened campaign has something to say. Raised on the UI thread.</summary>
    internal event EventHandler<string>? Reported;

    /// <summary>
    /// A campaign has opened and its folder check is done: the two numbers the stock reports on - its
    /// pictures and the free space on the drive (Part 7). Measured from what opening reads anyway, plus
    /// one system query; no other campaign is touched. Raised on the UI thread.
    /// </summary>
    internal event EventHandler<StockMeasure>? Measured;

    /// <summary>
    /// The open campaign's pictures changed without a campaign change - an intake ended, a picture was
    /// renamed or deleted. What lists the stock beside the panel (the tidy-up view, "Set up campaign")
    /// draws again. Raised on the UI thread.
    /// </summary>
    internal event EventHandler? StockChanged;

    /// <summary>
    /// Whether an intake is running. Meanwhile no campaign is switched, renamed or deleted: every single
    /// picture of a run goes to whatever stock is open at that moment (<see cref="OpenStock"/>), so a
    /// switch would split the run over two campaigns, and a delete would have the next picture create
    /// an unnamed campaign right after the DM deleted his (Part 3: the first picture creates one "while
    /// none is open", not after an explicit delete). Set by the panel that runs the intake.
    /// </summary>
    internal bool Taking
    {
        get => _taking;
        set
        {
            var ended = _taking && !value;

            _taking = value;

            if (ended)
            {
                StockChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Why a campaign may not be switched, renamed or deleted right now, or <see langword="null"/>.</summary>
    internal string? Refusal => Taking ? Strings.CampaignIntakeRunning : null;

    /// <summary>A picture of the open campaign was renamed or deleted.</summary>
    internal void StockEdited() => StockChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Free space on the drive the campaigns lie on, in bytes - one system query. A drive that cannot
    /// be asked reports as roomy rather than as full: a false alarm about the disk would be worse than none.
    /// </summary>
    internal long FreeSpace()
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(BasePath)) ?? BasePath).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return long.MaxValue;
        }
    }

    /// <summary>
    /// "Check folder again" (Part 3): the name match of the opening once more - phases 1 and 2 - and
    /// its report in the box beside the stage. Off the UI thread, like the first one, and after it:
    /// pressed while the opening's check still runs, it waits for that one.
    /// </summary>
    internal Task RescanAsync() =>
        Open is { } campaign ? CheckAsync(campaign, again: true) : Task.CompletedTask;

    /// <summary>"Verify stock" (Part 3) for the open campaign - every file against its hash, off the UI thread.</summary>
    internal async Task<StockVerification> VerifyAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        var campaign = Open ?? throw new InvalidOperationException("No campaign is open.");
        var result = await Task.Run(() => campaign.Store.VerifyAsync(progress, cancellationToken), cancellationToken).ConfigureAwait(true);

        ControlLog.StockVerified(_logger, campaign.Name, result.Checked, result.Damaged.Count);

        return result;
    }

    /// <summary>Every campaign, for the switcher - one directory listing (Part 3).</summary>
    internal IReadOnlyList<CampaignSummary> List() => _library.List();

    /// <summary>How the switcher calls a campaign: its name, or for an unnamed one when it began and how much is in it.</summary>
    internal static string Label(CampaignSummary summary) =>
        !summary.Unnamed
            ? summary.Name
            : summary.CreatedAt is { } created
                ? Strings.DeskUnnamedBegun(created.LocalDateTime, summary.Pictures ?? 0)
                : Strings.DeskUnnamed;

    /// <summary>The open campaign's name as the head shows it.</summary>
    internal string? OpenLabel => Open is { } open
        ? open.Unnamed ? Strings.DeskUnnamedCampaign(open.Store.CreatedAt.LocalDateTime) : open.Name
        : null;

    /// <summary>
    /// Opens a campaign. From a campaign that is open this is a SWITCH, and a switch clears the table
    /// everywhere first - the pictures there come from the stock about to close; the question in front
    /// of it is the caller's. From none it opens straight away and the table stays: a table taken over
    /// after a restart may well belong to the campaign being opened.
    /// </summary>
    /// <returns>Why it would not open, or <see langword="null"/>.</returns>
    internal async Task<string?> SwitchAsync(string name)
    {
        if (Refusal is { } refused)
        {
            return refused;
        }

        OpenCampaign campaign;

        try
        {
            campaign = _library.Open(name);
        }
        catch (CampaignSchemaException newer)
        {
            ControlLog.CampaignRefused(_logger, name, newer.Message);

            return newer.Message;
        }
        catch (DirectoryNotFoundException gone)
        {
            return gone.Message;
        }
        catch (CampaignUnavailableException unavailable)
        {
            // A file of the campaign held by another program while it opens, a folder the account may
            // not read: the library's one stated refusal, said - never a crash. Nothing was changed.
            ControlLog.CampaignRefused(_logger, name, unavailable.Message);

            return unavailable.Message;
        }

        if (Open is not null)
        {
            await _session.ClearEverywhereAsync(CancellationToken.None).ConfigureAwait(true);
        }

        Arrive(campaign);

        return null;
    }

    /// <summary>Where the campaigns lie now.</summary>
    internal string BasePath => _library.BasePath;

    /// <summary>
    /// Closes the open campaign the way a switch does - the table cleared everywhere, undo ended -
    /// and opens none. The condition for moving the campaign folder: nobody moves a folder the hub is
    /// delivering pictures out of (Part 7).
    /// </summary>
    internal async Task CloseAsync()
    {
        if (Open is null)
        {
            return;
        }

        await _session.ClearEverywhereAsync(CancellationToken.None).ConfigureAwait(true);

        Become(null);
    }

    /// <summary>The campaigns lie somewhere else now; the switcher lists what is there.</summary>
    internal void Relocate(string path)
    {
        _library.Relocate(path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Renames the open campaign and its folder - also how an unnamed one gets its first name.
    /// </summary>
    internal CampaignOutcome Rename(string newName)
    {
        if (Open is not { } open)
        {
            return CampaignOutcome.BadName;
        }

        // The intake writing into the folder holds the old place; a move between two of its writes
        // would split the pictures over two folders. The folder is in use - which is what Busy says.
        if (Taking)
        {
            return CampaignOutcome.Busy;
        }

        var (outcome, renamed) = _library.Rename(open, newName);

        if (outcome is CampaignOutcome.Done)
        {
            ControlLog.CampaignRenamed(_logger, open.Name, renamed.Name);
            Become(renamed);
        }

        return outcome;
    }

    /// <summary>
    /// Deletes the open campaign: the table is cleared everywhere, the folder goes, and no campaign
    /// is open afterwards - the same state as after the start, with the same two ways on (Part 7).
    /// Creating an empty one in its place would be a folder nobody ordered.
    /// <para>
    /// <b>All or nothing, as far as the file system lets it</b>: a display still downloading a map, a
    /// verify reading a file - a recursive delete would stop at the first file held open and leave
    /// the campaign half gone, the one grip that cannot be taken back done by half (Part 7). The
    /// library moves the folder aside in one rename first, which Windows refuses while anything in it
    /// is open (<see cref="CampaignLibrary.Delete"/>) - the one way of deleting, not a second probe
    /// beside it. <b>Asked before the table is cleared</b>: refused, the campaign is open as it was,
    /// the table too, and the DM is told. The table goes only with the folder.
    /// </para>
    /// <para>
    /// What can still fail is a file taken hold of between the move and the delete. Then the library
    /// has put what is left back under its name, its stock stays closed, and the campaign is not
    /// opened again over a half-deleted folder: the table is cleared, none is open, and the DM is told
    /// where the rest lies.
    /// </para>
    /// </summary>
    /// <returns>Why it was not deleted, or not completely, or <see langword="null"/>.</returns>
    internal async Task<string?> DeleteAsync()
    {
        if (Open is not { } open)
        {
            return null;
        }

        if (Refusal is { } refused)
        {
            return refused;
        }

        var outcome = _library.Delete(open);

        // Refused before anything went: the library opened the stock again, and nothing - not even the
        // table - has changed.
        if (outcome is CampaignOutcome.Busy && !open.Store.IsClosed)
        {
            return Strings.CampaignDeleteHeld(open.Name);
        }

        await _session.ClearEverywhereAsync(CancellationToken.None).ConfigureAwait(true);

        Become(null);

        if (outcome is not CampaignOutcome.Done)
        {
            var left = Directory.Exists(open.Directory) ? open.Directory : BasePath;

            ControlLog.CampaignDeleteIncomplete(
                _logger, open.Name, $"a file in it was taken hold of while it was being deleted; what is left lies in {left}.");

            return Strings.CampaignDeleteIncomplete(open.Name, left);
        }

        ControlLog.CampaignDeleted(_logger, open.Name);

        return null;
    }

    /// <summary>
    /// Runs a write into the open campaign's folder - a scene, a layout, the inventory - and turns
    /// what the disk refuses (full, a file held by a sync client or a virus scanner) into the
    /// sentence the DM reads, never a crash out of a menu (<see cref="SceneShelf"/> and
    /// <see cref="AssetStore"/> catch nothing). <paramref name="refused"/> is the reason, or null.
    /// </summary>
    internal static bool Writes<T>(Func<T> write, [MaybeNullWhen(false)] out T result, [NotNullWhen(false)] out string? refused)
    {
        ArgumentNullException.ThrowIfNull(write);

        try
        {
            result = write();
            refused = null;

            return true;
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            result = default;
            refused = failed.Message;

            return false;
        }
    }

    /// <summary>
    /// A save or rename on the shelf, the same way: the shelf states the disk's refusal as
    /// <see cref="ShelfOutcome.Failed"/> rather than throwing, and that becomes the sentence too - a
    /// Failed taken for "nothing to say" would be a scene the DM believes saved. Any other outcome is
    /// the caller's (Taken asks, Done says so).
    /// </summary>
    internal static bool Shelves(Func<ShelfOutcome> write, out ShelfOutcome outcome, [NotNullWhen(false)] out string? refused)
    {
        if (!Writes(write, out outcome, out refused))
        {
            return false;
        }

        if (outcome is ShelfOutcome.Failed)
        {
            refused = Strings.ShelfRefused;

            return false;
        }

        return true;
    }

    /// <summary>
    /// A delete on the shelf: it answers false both for a name that is not there and for a file that
    /// would not go - the second one stays listed, and that is the refusal. <paramref name="listed"/>
    /// asks the shelf afterwards.
    /// </summary>
    internal static bool Removes(Func<bool> delete, Func<bool> listed, [NotNullWhen(false)] out string? refused)
    {
        ArgumentNullException.ThrowIfNull(listed);

        if (!Writes(delete, out var deleted, out refused))
        {
            return false;
        }

        if (!deleted && listed())
        {
            refused = Strings.ShelfRefused;

            return false;
        }

        return true;
    }

    /// <summary>
    /// The first picture while none is open: a new unnamed campaign, opened (Part 3). Called from the
    /// ingest, which may run off the UI thread - the change is announced on it.
    /// </summary>
    internal AssetStore EnsureOpen()
    {
        lock (_gate)
        {
            if (Open is { } open)
            {
                return open.Store;
            }

            var created = _library.CreateUnnamed();

            Stock.Current = created.Store;
            Open = created;

            _ = _dispatcher.InvokeAsync(() =>
            {
                ControlLog.CampaignOpened(_logger, created.Name, 0, 0);
                Changed?.Invoke(this, EventArgs.Empty);
            });

            return created.Store;
        }
    }

    private void Arrive(OpenCampaign campaign)
    {
        Become(campaign);

        ControlLog.CampaignOpened(_logger, campaign.Name, campaign.Store.Count, campaign.Shelf.Scenes.Count);

        // Phase 2 off the UI thread: the campaign is open at once and nothing waits on the check
        // (Part 3). Its report comes when it is done, in the box beside the stage.
        _ = CheckAsync(campaign, again: false);
    }

    /// <summary>
    /// The folder check - phase 2 of the opening, or "Check folder again" - off the UI thread and after
    /// the one before it. <b>Never a crash</b>: a file Explorer is still copying in cannot be read yet,
    /// and that is said in the box beside the stage instead (Part 3: the check reports there and
    /// destroys nothing). <b>Only for the campaign still open</b>: a switch while the check ran has
    /// its own report, and the old one's would stand beside the stage under the wrong campaign.
    /// </summary>
    private Task CheckAsync(OpenCampaign campaign, bool again)
    {
        var before = _checking;
        var check = Task.Run(async () =>
        {
            await before.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            string? words;
            var adopted = 0;

            try
            {
                var findings = again ? Reconciliation.Survey(campaign.Store) : campaign.Findings;
                var report = await Reconciliation.SettleAsync(campaign.Store, campaign.Shelf, findings).ConfigureAwait(false);

                ControlLog.CampaignReconciled(
                    _logger, report.Adopted, report.Duplicates, report.Missing.Count, report.Foreign.Count, report.Reconstructed);

                // Broken off because the campaign closed under it - switched, renamed, deleted, moved:
                // half a report, about a stock no longer in use. Nothing to say beside the stage.
                words = report.Interrupted ? null : Words(campaign, report);
                adopted = report.Adopted;
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
            {
                ControlLog.CampaignCheckFailed(_logger, campaign.Name, failed.Message);
                words = Strings.CampaignCheckFailed(campaign.Name, failed.Message);
            }

            await _dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(Open, campaign))
                {
                    return;
                }

                if (words is not null)
                {
                    Reported?.Invoke(this, words);
                }

                if (!again)
                {
                    Measured?.Invoke(this, new StockMeasure(campaign.Store.Count, FreeSpace()));
                }

                // Pictures taken in from the folder belong in the lists now.
                if (adopted > 0)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
        });

        _checking = check;

        return check;
    }

    private void Become(OpenCampaign? campaign)
    {
        lock (_gate)
        {
            // The campaign left behind takes no more writes: an ingest still decoding or a folder check
            // still settling would otherwise write into its folder afterwards - and after a move of the
            // campaigns into a folder that is no longer theirs (OpenCampaign.Close). Rename and delete
            // closed it already; closing twice is nothing.
            if (Open is { } leaving && !ReferenceEquals(leaving, campaign))
            {
                leaving.Close();
            }

            Open = campaign;
            Stock.Current = campaign?.Store;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The report: the campaign's numbers on arriving - the moment they count - and what the folder
    /// check did, in one line (Part 3, Part 7).
    /// </summary>
    private static string Words(OpenCampaign campaign, CampaignReport report)
    {
        var size = campaign.Store.Entries.Sum(entry => entry.Bytes);
        var parts = new List<string>
        {
            Strings.DeskOpened(campaign.Name, campaign.Store.Count, size / (1024d * 1024), campaign.Shelf.Scenes.Count),
        };

        if (report.Reconstructed)
        {
            parts.Add(Strings.DeskReconstructed);
        }

        if (report.Adopted > 0)
        {
            parts.Add(Strings.DeskAdopted(report.Adopted));
        }

        if (report.Duplicates > 0)
        {
            parts.Add(Strings.DeskDuplicates(report.Duplicates));
        }

        if (report.Missing.Count > 0)
        {
            parts.Add(Strings.DeskMissing(report.Missing.Count, FirstNames(report.Missing)));
        }

        if (report.Foreign.Count > 0)
        {
            parts.Add(Strings.DeskForeign(report.Foreign.Count));
        }

        if (report.UnreadableScenes.Count > 0)
        {
            parts.Add(Strings.DeskUnreadableScenes(report.UnreadableScenes.Count, string.Join(", ", report.UnreadableScenes)));
        }

        if (report.Kept.Count > 0)
        {
            parts.Add(Strings.DeskKept(report.Kept.Count));
        }

        // Locked, an offline placeholder, gone since the first look: named, because the DM is the one
        // who knows which program holds it - and the next check tries them again (Part 3, phase 3).
        if (report.Skipped.Count > 0)
        {
            parts.Add(Strings.DeskSkipped(report.Skipped.Count, FirstNames(report.Skipped)));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The first few names of a list, for one line - the rest as "...".</summary>
    private static string FirstNames(IReadOnlyList<string> names) =>
        string.Join(", ", names.Take(5)) + (names.Count > 5 ? ", ..." : string.Empty);
}

/// <summary>What the stock reports on, measured when a campaign opens (Part 7).</summary>
/// <param name="Pictures">Pictures in the campaign just opened.</param>
/// <param name="FreeBytes">Free space on the drive the campaigns lie on.</param>
internal sealed record StockMeasure(int Pictures, long FreeBytes);
