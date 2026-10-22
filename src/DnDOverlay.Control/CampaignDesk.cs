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
        OpenCampaign campaign;

        try
        {
            campaign = _library.Open(name);
        }
        catch (CampaignSchemaException refused)
        {
            ControlLog.CampaignRefused(_logger, name, refused.Message);

            return refused.Message;
        }
        catch (System.IO.DirectoryNotFoundException gone)
        {
            return gone.Message;
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
    /// </summary>
    internal async Task DeleteAsync()
    {
        if (Open is not { } open)
        {
            return;
        }

        await _session.ClearEverywhereAsync(CancellationToken.None).ConfigureAwait(true);

        Become(null);
        _library.Delete(open);

        ControlLog.CampaignDeleted(_logger, open.Name);
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
        _ = Task.Run(async () =>
        {
            var report = await Reconciliation.SettleAsync(campaign.Store, campaign.Shelf, campaign.Findings).ConfigureAwait(false);

            ControlLog.CampaignReconciled(
                _logger, report.Adopted, report.Duplicates, report.Missing.Count, report.Foreign.Count, report.Reconstructed);

            var words = Words(campaign, report);

            await _dispatcher.InvokeAsync(() =>
            {
                Reported?.Invoke(this, words);

                // Pictures taken in from the folder belong in the lists now.
                if (report.Adopted > 0 && ReferenceEquals(Open, campaign))
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
        });
    }

    private void Become(OpenCampaign? campaign)
    {
        lock (_gate)
        {
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
            parts.Add(Strings.DeskMissing(
                report.Missing.Count,
                string.Join(", ", report.Missing.Take(5)) + (report.Missing.Count > 5 ? ", ..." : string.Empty)));
        }

        if (report.Foreign.Count > 0)
        {
            parts.Add(Strings.DeskForeign(report.Foreign.Count));
        }

        if (report.UnreadableScenes.Count > 0)
        {
            parts.Add(Strings.DeskUnreadableScenes(report.UnreadableScenes.Count, string.Join(", ", report.UnreadableScenes)));
        }

        return string.Join(" · ", parts);
    }
}
