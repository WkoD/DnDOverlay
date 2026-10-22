using System.Text.RegularExpressions;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign;

/// <summary>What the first look at a campaign folder found - by names, without a byte read (Part 3, phase 1).</summary>
/// <param name="Orphans">Files that are not where the stock keeps an entry's picture: a picture laid in by hand, a crash between writing and recording, or a foreign file - which of these only phase 2 can say.</param>
/// <param name="Missing">Entries whose file is gone.</param>
public sealed record CampaignFindings(IReadOnlyList<string> Orphans, IReadOnlySet<AssetId> Missing);

/// <summary>
/// The report beside the stage after a campaign opened (Part 3, phase 3) - one report, not twenty
/// dialogs: "4 pictures taken in from the folder · 2 pictures missing · 1 foreign file ignored".
/// </summary>
/// <param name="Adopted">Pictures from the folder now in the stock - renamed to their hash and moved to <c>assets\</c>.</param>
/// <param name="Duplicates">Byte-identical copies of pictures already in the stock, removed.</param>
/// <param name="Missing">The names of entries whose file is gone - marked, not removed.</param>
/// <param name="Foreign">Files that are no picture, left where they are.</param>
/// <param name="Reconstructed">The inventory could not be read and was rebuilt from the folder.</param>
/// <param name="UnreadableScenes">Scene and layout files that would not read, left where they are.</param>
public sealed record CampaignReport(
    int Adopted,
    int Duplicates,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Foreign,
    bool Reconstructed,
    IReadOnlyList<string> UnreadableScenes)
{
    /// <summary>
    /// Pictures that are in the stock but whose file was left where it lies, because what is in
    /// <c>assets\</c> is not byte for byte that file: a token the picture was taken out of (a
    /// <c>.rptok</c> is read, never written, Part 3), or a format converted on the way in. Part 3 removes
    /// byte-identical copies only; anything else would lose what the stock's copy does not carry.
    /// </summary>
    public IReadOnlyList<string> Kept { get; init; } = [];

    /// <summary>Whether there is anything to say at all - a campaign without findings opens without a report.</summary>
    public bool IsEmpty =>
        Adopted == 0 && Duplicates == 0 && Missing.Count == 0 && Foreign.Count == 0 && !Reconstructed && UnreadableScenes.Count == 0
        && Kept.Count == 0;
}

/// <summary>
/// Matches a campaign folder against its inventory when it opens (Part 3): <b>what is derived is
/// repaired without a word, what is primary is reported, and nothing is deleted unasked.</b>
/// <para>
/// Searched is the whole folder, recursively, leaving out our own subfolders - the DM may lay
/// pictures wherever it occurs to him. Known operating-system litter is not even mentioned, or
/// the report would fire for every folder Explorer has once looked at. No file system watcher: it
/// fires on our own writes too, and this is a walk one takes before the evening, not during it.
/// </para>
/// </summary>
public static partial class Reconciliation
{
    private static readonly HashSet<string> Litter = new(["Thumbs.db", "desktop.ini", ".DS_Store"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Phase 1: the file names against the entries - three sets, matching, orphaned, missing. Not a
    /// byte is read, so the campaign is open at once and nothing waits on the check.
    /// </summary>
    public static CampaignFindings Survey(AssetStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var root = store.Directory;
        var skipped = new[]
        {
            Path.Combine(root, "scenes"),
            Path.Combine(root, "layouts"),
            Path.Combine(root, "assets", "thumbs"),
        };

        var orphans = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);

            if (name.StartsWith("inventory.json", StringComparison.OrdinalIgnoreCase)
                || Litter.Contains(name)
                || skipped.Any(folder => file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                || store.Keeps(file))
            {
                continue;
            }

            orphans.Add(file);
        }

        return new CampaignFindings(orphans, store.Missing);
    }

    /// <summary>
    /// Phase 2: what happens by itself. Orphaned pictures come in without a question, through the
    /// same ingest as a drop - renamed to their hash and moved into <c>assets\</c>, because the folder
    /// is our stock and not a letterbox, and without the rename the same file would look orphaned on
    /// every opening. Byte-identical copies of what is there are removed. <b>Only</b> a file whose bytes
    /// now lie in <c>assets\</c> unchanged goes: a token or a converted picture stays and is named
    /// (<see cref="CampaignReport.Kept"/>), because the check is non-destructive (Part 3). A picture already named by
    /// its hash keeps that identity and takes its name from the scene that uses it. Foreign files
    /// stay untouched, missing entries stay marked, thumbnails are repaired.
    /// </summary>
    public static async Task<CampaignReport> SettleAsync(
        AssetStore store,
        SceneShelf shelf,
        CampaignFindings findings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(shelf);
        ArgumentNullException.ThrowIfNull(findings);

        var adopted = 0;
        var duplicates = 0;
        var foreign = new List<string>();
        var kept = new List<string>();
        var done = 0;

        // Files in assets\ first: a hash-named file there is the stock's own, and a copy of it somewhere
        // else - a backup of the folder - is then judged against it rather than racing it for the place.
        var assets = Path.Combine(store.Directory, "assets") + Path.DirectorySeparatorChar;

        foreach (var file in findings.Orphans.OrderBy(file => file.StartsWith(assets, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Taken into the stock meanwhile, by an earlier file of this run or a check beside it: it is
            // the stock's own file now, and nothing below may treat it as a copy.
            if (store.Keeps(file))
            {
                progress?.Report(++done);
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(file);
            var hashNamed = HashName().IsMatch(stem);
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);

            if (hashNamed && AssetStore.IsDeliveryFormat(Path.GetExtension(file).TrimStart('.')))
            {
                var asset = new AssetId(stem.ToLowerInvariant());

                if (store.Knows(asset))
                {
                    // In the stock already. A byte-identical copy goes; the same file coming back heals
                    // the entry whose file was missing; anything else under its name is not ours to
                    // judge and stays (Part 3).
                    if (store.Holds(asset, bytes))
                    {
                        if (RemoveCopy(store, file))
                        {
                            duplicates++;
                        }
                    }
                    else if (store.Restore(asset, file, bytes))
                    {
                        adopted++;
                    }
                    else
                    {
                        foreign.Add(file);
                    }
                }
                else if (store.AdoptStored(file, NameFromScenes(shelf, stem) ?? stem[..12], AddedAt(file)) is not null)
                {
                    adopted++;
                }
                else
                {
                    foreign.Add(file);
                }
            }
            else
            {
                // A hash-named file in a format the stock does not deliver comes through the intake like
                // any other - converted, limited - and keeps the scene's name for it.
                var proposed = hashNamed ? NameFromScenes(shelf, stem) ?? stem[..12] : stem;
                var result = await store.IngestAsync(bytes, proposed, cancellationToken).ConfigureAwait(false);

                if (result is IngestResult.Taken taken)
                {
                    // New to the stock - or an entry whose file was missing, healed by this one: that is
                    // the picture coming back (Part 3), not a copy of something that was there.
                    var isNew = !taken.AlreadyPresent || findings.Missing.Contains(taken.Asset.AssetId);

                    if (isNew)
                    {
                        adopted++;
                    }

                    // Removed only when its bytes now lie in assets\ unchanged - "renamed to its hash and
                    // moved", "byte-identical copies removed" (Part 3). A token, or a picture converted on
                    // the way in, is not what the stock holds, and stays where the DM put it.
                    if (store.Holds(taken.Asset.AssetId, bytes))
                    {
                        if (RemoveCopy(store, file) && !isNew)
                        {
                            duplicates++;
                        }
                    }
                    else
                    {
                        kept.Add(file);
                    }
                }
                else
                {
                    foreign.Add(file);
                }
            }

            progress?.Report(++done);
        }

        store.RepairThumbnails();

        var names = store.Entries.ToDictionary(entry => entry.AssetId, entry => entry.Name, StringComparer.Ordinal);
        var missing = store.Missing.Select(asset => names.GetValueOrDefault(asset.Value, asset.Value)).Order(StringComparer.CurrentCulture).ToList();

        return new CampaignReport(
            adopted,
            duplicates,
            missing,
            [.. foreign.Select(path => Path.GetRelativePath(store.Directory, path))],
            store.Reconstructed,
            [.. shelf.Unreadable.Select(path => Path.GetRelativePath(store.Directory, path))])
        {
            Kept = [.. kept.Select(path => Path.GetRelativePath(store.Directory, path))],
        };
    }

    /// <summary>
    /// Removes a byte-identical copy of what the stock holds - and never the stock's own file, which
    /// is the same bytes by definition. False when it was not removed.
    /// </summary>
    private static bool RemoveCopy(AssetStore store, string file)
    {
        if (store.Keeps(file))
        {
            return false;
        }

        File.Delete(file);

        return true;
    }

    /// <summary>When a picture adopted under its hash name came in: its file's time, the only clue left (Part 3).</summary>
    private static DateTimeOffset AddedAt(string file) => new(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);

    /// <summary>The name a scene of this campaign gives a picture known only by its hash (Part 3).</summary>
    private static string? NameFromScenes(SceneShelf shelf, string hash)
    {
        foreach (var (_, _, scene) in shelf.All)
        {
            if (scene.Items.OfType<ImageItem>().FirstOrDefault(item => string.Equals(item.AssetId.Value, hash, StringComparison.OrdinalIgnoreCase)) is { } item)
            {
                return item.Name;
            }

            if (scene.Background is { } background
                && string.Equals(background.AssetId.Value, hash, StringComparison.OrdinalIgnoreCase)
                && background.Name is { } name)
            {
                return name;
            }
        }

        return null;
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex HashName();
}
