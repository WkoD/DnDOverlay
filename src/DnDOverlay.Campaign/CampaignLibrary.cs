namespace DnDOverlay.Campaign;

/// <summary>
/// A campaign as the switcher lists it - the name and nothing else, because listing costs one
/// directory listing and opens no foreign inventory (Part 3). An unnamed one is told apart by its
/// creation date and its count instead, read from its small inventory; without them two of them
/// could not be told apart at all.
/// </summary>
public sealed record CampaignSummary(string Name, bool Unnamed, DateTimeOffset? CreatedAt = null, int? Pictures = null);

/// <summary>The campaign that is open: its folder, its stock, its scenes.</summary>
public sealed class OpenCampaign
{
    internal OpenCampaign(string name, AssetStore store, SceneShelf shelf, CampaignFindings findings)
    {
        Name = name;
        Store = store;
        Shelf = shelf;
        Findings = findings;
    }

    /// <summary>The name - which is also the folder's (Part 3).</summary>
    public string Name { get; }

    /// <summary>Whether it still carries the provisional name it got with its first picture.</summary>
    public bool Unnamed => CampaignLibrary.IsUnnamed(Name);

    /// <summary>Its pictures and inventory.</summary>
    public AssetStore Store { get; }

    /// <summary>Its scenes and layouts, read completely on open.</summary>
    public SceneShelf Shelf { get; }

    /// <summary>What the first look at the folder found; <see cref="Reconciliation.SettleAsync"/> does the rest.</summary>
    public CampaignFindings Findings { get; }

    /// <summary>Where it lies.</summary>
    public string Directory => Store.Directory;

    /// <summary>
    /// Ends every write into its folder - for the caller that switches away from it, closes it or
    /// moves the campaigns: an ingest still decoding and a folder check still settling would otherwise
    /// write into the old path afterwards (<see cref="AssetStore.Close"/>). Rename and delete close it
    /// themselves.
    /// </summary>
    public void Close() => Store.Close();
}

/// <summary>What became of a rename or a delete.</summary>
public enum CampaignOutcome
{
    /// <summary>Done.</summary>
    Done,

    /// <summary>Another campaign has that name - refused before anything happened (Part 3).</summary>
    Taken,

    /// <summary>The name cannot be a folder name; <see cref="FileNames.Check"/> says why.</summary>
    BadName,

    /// <summary>The folder is in use - a picture being sent right now; try again.</summary>
    Busy,
}

/// <summary>
/// The campaigns under one base path (Part 3): a folder each, named as the campaign is called.
/// <para>
/// <b>Edited is only what is open.</b> Rename and delete take the open campaign and no name - the
/// rule stands in the signature, not in a check a caller could forget. The one exception is the
/// list, so one sees where one can switch to; nothing is touched from it.
/// </para>
/// </summary>
/// <param name="openStore">Opens the stock of a folder - handed in, because the stock needs the codec and the unpacker the control builds.</param>
/// <param name="baseIsDefault">
/// Whether <paramref name="basePath"/> is the default place, which comes into being with the first
/// picture. A place the DM chose existed when it was chosen; if it is gone, it is not reachable, and
/// nothing is created there (<see cref="Reachable"/>).
/// </param>
public sealed class CampaignLibrary(string basePath, Func<string, AssetStore> openStore, TimeProvider time, bool baseIsDefault = true)
{
    /// <summary>
    /// What a campaign is called before the DM names it: <c>~unbenannt</c>, <c>~unbenannt 2</c>, ...
    /// (Part 3). The tilde sorts it apart from the named ones and no typed name is likely to start with it.
    /// </summary>
    public const string UnnamedStem = "~unbenannt";

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private bool _baseIsDefault = baseIsDefault;

    /// <summary>Where the campaigns lie.</summary>
    public string BasePath { get; private set; } = basePath;

    /// <summary>
    /// Whether the campaigns folder can be reached: it is there, or it is the default place and the
    /// folder above it is there. <b>Not</b> reachable is a stick pulled, a network drive gone, a chosen
    /// folder that no longer exists - and then it is reported and <b>nothing is created</b>: a quiet
    /// new folder would be a second, empty stock, and the DM would lay pictures into a place he never
    /// finds again (Part 3). An empty <see cref="List"/> does not tell the two apart; this does.
    /// </summary>
    public bool Reachable
    {
        get
        {
            try
            {
                if (Directory.Exists(BasePath))
                {
                    return true;
                }

                // A drive root that is not there has no folder above it - the drive is gone.
                return _baseIsDefault
                    && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(BasePath))) is { } parent
                    && Directory.Exists(parent);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Points the library at another base path - after the campaigns were moved there, or when the DM
    /// chose only to change the place (Part 7). No campaign may be open: the caller closes it first. A
    /// place set this way is a chosen one (see <see cref="Reachable"/>).
    /// </summary>
    public void Relocate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        BasePath = path;
        _baseIsDefault = false;
    }

    /// <summary>Whether this name is a provisional one.</summary>
    public static bool IsUnnamed(string name) =>
        name.StartsWith(UnnamedStem, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every campaign, by name - one directory listing, plus the small inventory of an unnamed one.</summary>
    public IReadOnlyList<CampaignSummary> List()
    {
        if (!Directory.Exists(BasePath))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateDirectories(BasePath)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Select(name => IsUnnamed(name) ? Unnamed(name) : new CampaignSummary(name, Unnamed: false))
                .OrderBy(summary => summary.Unnamed)
                .ThenBy(summary => summary.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <summary>
    /// Opens a campaign: its inventory, every scene and layout, and the first look at its folder
    /// (phases 0 and 1). The rest of the reconciliation is <see cref="Reconciliation.SettleAsync"/>,
    /// so the campaign is open at once.
    /// </summary>
    /// <exception cref="CampaignSchemaException">Written by a newer version - the one hard no (Part 3).</exception>
    /// <exception cref="DirectoryNotFoundException">There is no campaign of that name.</exception>
    public OpenCampaign Open(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var directory = Path.Combine(BasePath, name);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(Strings.NoSuchCampaign(name, BasePath));
        }

        // The scenes first: a newer schema there refuses before the stock is touched.
        var shelf = SceneShelf.Open(directory, time);
        var store = openStore(directory);

        return new OpenCampaign(name, store, shelf, Reconciliation.Survey(store));
    }

    /// <summary>
    /// A new unnamed campaign, for the first picture that comes in while none is open (Part 3). An
    /// unnamed one that exists is NOT reused - forty pictures from three months ago would suddenly
    /// stand beside the one just pasted; this one gets the next number. Its folder comes into being
    /// with this, which is why this is called by the first picture and not by looking around.
    /// </summary>
    /// <exception cref="CampaignBaseUnreachableException">The campaigns folder cannot be reached; nothing was created (Part 3).</exception>
    public OpenCampaign CreateUnnamed()
    {
        if (!Reachable)
        {
            throw new CampaignBaseUnreachableException(Strings.BaseUnreachable(BasePath));
        }

        Directory.CreateDirectory(BasePath);

        var taken = Directory.EnumerateDirectories(BasePath).Select(Path.GetFileName).OfType<string>().ToList();
        var name = UnnamedStem;

        for (var number = 2; taken.Contains(name, FileNames.Comparer); number++)
        {
            name = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{UnnamedStem} {number}");
        }

        Directory.CreateDirectory(Path.Combine(BasePath, name));

        return Open(name);
    }

    /// <summary>
    /// Renames the open campaign and its folder - two names for one thing would be the divergence
    /// nobody can resolve a year later (Part 7). A taken name is refused before anything happens.
    /// The campaign comes back opened from its new place.
    /// </summary>
    public (CampaignOutcome Outcome, OpenCampaign Campaign) Rename(OpenCampaign campaign, string newName)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        if (FileNames.Check(newName) is not null)
        {
            return (CampaignOutcome.BadName, campaign);
        }

        var sameOne = FileNames.Comparer.Equals(campaign.Name, newName);

        if (!sameOne && List().Any(other => FileNames.Comparer.Equals(other.Name, newName)))
        {
            return (CampaignOutcome.Taken, campaign);
        }

        var from = campaign.Directory;
        var to = Path.Combine(BasePath, newName);

        // Closed first: an ingest that finished after the move would write into the old path and bring
        // the folder back as a second campaign. The campaign comes back opened from the new place; if
        // the move is refused, the old stock takes writes again.
        campaign.Close();

        try
        {
            MoveFolder(from, to, caseOnly: sameOne);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            campaign.Store.Reopen();

            return (CampaignOutcome.Busy, campaign);
        }

        try
        {
            return (CampaignOutcome.Done, Open(newName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CampaignSchemaException)
        {
            // Moved, but not readable at the new place right now - a sync client took the inventory the
            // moment it arrived. Back under the old name, so the folder and the campaign the desk holds
            // agree again; the DM tries once more.
            try
            {
                MoveFolder(to, from, caseOnly: sameOne);
                campaign.Store.Reopen();
            }
            catch (Exception back) when (back is IOException or UnauthorizedAccessException)
            {
            }

            return (CampaignOutcome.Busy, campaign);
        }
    }

    /// <summary>
    /// Moves a campaign folder. A change of case only goes through a temporary name, or a
    /// case-insensitive file system calls it a move onto itself - and if the second step is refused,
    /// the first is taken back, so the folder never stays behind under the temporary name, where the
    /// switcher would list it as a campaign of its own and the open one would point at nothing.
    /// </summary>
    private static void MoveFolder(string from, string to, bool caseOnly)
    {
        if (!caseOnly)
        {
            Directory.Move(from, to);

            return;
        }

        var between = from + ".renaming";

        Directory.Move(from, between);

        try
        {
            Directory.Move(between, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Directory.Move(between, from);
            throw;
        }
    }

    /// <summary>
    /// Deletes the open campaign with its folder - pictures, scenes, layouts. The one grip in the
    /// program that cannot be taken back; the question in front of it is the control's, and it asks
    /// for the name typed (Part 7).
    /// <para>
    /// <b>All or nothing, as far as the file system lets it be.</b> The folder is first moved aside in
    /// one rename, which the system refuses while anything inside is held open - a display still
    /// downloading a picture, "verify stock" reading one. Then nothing is deleted and the answer is
    /// <see cref="CampaignOutcome.Busy"/>, rather than a folder half gone and an exception.
    /// </para>
    /// </summary>
    /// <returns><see cref="CampaignOutcome.Done"/>, or <see cref="CampaignOutcome.Busy"/> with the campaign as it was and its stock open again.</returns>
    public CampaignOutcome Delete(OpenCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        // Only ever a folder directly under this base path: a recursive delete is the one call here
        // that could take something that is not a campaign.
        if (!FileNames.IsDirectlyUnder(campaign.Directory, BasePath))
        {
            throw new InvalidOperationException($"{campaign.Directory} is not a campaign under {BasePath}.");
        }

        // Nothing may write the folder back after it is gone - the delete is final (Part 3).
        campaign.Close();

        var from = campaign.Directory;
        var aside = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{from}.deleting-{Guid.NewGuid():N}");

        try
        {
            Directory.Move(from, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            campaign.Store.Reopen();

            return CampaignOutcome.Busy;
        }

        try
        {
            Directory.Delete(aside, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Something took hold of a file in the moment between: what is left goes back under its
            // name, where the DM finds it, rather than standing in the switcher under a scratch name.
            try
            {
                Directory.Move(aside, from);
            }
            catch (Exception back) when (back is IOException or UnauthorizedAccessException)
            {
            }

            return CampaignOutcome.Busy;
        }

        return CampaignOutcome.Done;
    }

    private CampaignSummary Unnamed(string name)
    {
        try
        {
            var path = Path.Combine(BasePath, name, "inventory.json");

            if (File.Exists(path)
                && System.Text.Json.JsonSerializer.Deserialize<InventoryDocument>(File.ReadAllBytes(path), Web) is { } document)
            {
                return new CampaignSummary(name, Unnamed: true, document.CreatedAt, document.Entries.Count);
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new CampaignSummary(name, Unnamed: true);
    }
}

/// <summary>
/// The campaigns folder cannot be reached - a drive pulled, a network drive gone, a chosen folder
/// removed. Nothing was created in its place (Part 3); the message names the folder.
/// </summary>
public sealed class CampaignBaseUnreachableException : Exception
{
    public CampaignBaseUnreachableException(string message)
        : base(message)
    {
    }

    public CampaignBaseUnreachableException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public CampaignBaseUnreachableException()
    {
    }
}
