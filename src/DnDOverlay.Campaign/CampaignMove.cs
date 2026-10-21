namespace DnDOverlay.Campaign;

/// <summary>What a move of the campaign folder would involve - the question asked before it (Part 7).</summary>
/// <param name="Campaigns">The campaigns at the old place, by folder name.</param>
/// <param name="Bytes">Their size together.</param>
/// <param name="Collisions">Campaigns of the same name already at the new place - the move does not begin while there are any.</param>
/// <param name="RoomForNames">
/// How many characters are left, within the 260 Windows allows, for a campaign's name and a scene's
/// name together once the new place is that deep. Few means a scene that cannot be saved months
/// later, from a decision nobody connects with this dialog any more.
/// </param>
public sealed record MovePlan(IReadOnlyList<string> Campaigns, long Bytes, IReadOnlyList<string> Collisions, int RoomForNames)
{
    /// <summary>Below this the question says so: a campaign name and a scene name of forty characters each still fit.</summary>
    public const int TightRoom = 80;
}

/// <summary>What a move left to do after the new place was taken into use: the old copies to remove.</summary>
public sealed record MoveResult(bool Copied, IReadOnlyList<string> SourcesToRemove);

/// <summary>The move could not begin or did not finish; nothing was changed at either place.</summary>
public sealed class CampaignMoveException(string message, Exception? inner = null) : Exception(message, inner)
{
    public CampaignMoveException()
        : this("The campaigns could not be moved.")
    {
    }

    public CampaignMoveException(string message)
        : this(message, null)
    {
    }
}

/// <summary>
/// Moves the campaigns to a new base path (Part 7, "moving the campaign folder"): <b>renamed where
/// they can be, copied only where they must</b>.
/// <para>
/// <b>Which case applies is tried, not worked out.</b> The rename is attempted, and only if the system
/// refuses it - source and target on different volumes - does the expensive way begin. A volume
/// detection of our own would be lied to by mount points, network drives and links; the cheap
/// attempt's failure is the more reliable answer and costs nothing. That this is platform-neutral
/// falls out of it.
/// </para>
/// <para>
/// <b>All or nothing, in both columns.</b> A failed rename puts back what was already renamed; a
/// failed or broken-off copy takes away the copies begun. There is no state with a campaign half here
/// and half there, and none in which the path points at something incomplete: the sources go only
/// after the caller has taken the new place into use (<see cref="RemoveSources"/>).
/// </para>
/// <para>
/// <b>Checked by count and size, not by content</b> - hashing every file would double the reading for
/// something the file system already guards.
/// </para>
/// </summary>
public static class CampaignMove
{
    /// <summary>What has to stay free beyond the copies themselves.</summary>
    private const long Reserve = 64L * 1024 * 1024;

    /// <summary>Asks what a move from <paramref name="from"/> to <paramref name="to"/> would involve.</summary>
    public static MovePlan Plan(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var campaigns = Directory.Exists(from)
            ? Directory.GetDirectories(from).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

        var taken = Directory.Exists(to)
            ? Directory.GetDirectories(to).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var bytes = campaigns.Sum(name => Size(Path.Combine(from, name)));

        // to \ campaign \ scenes \ scene .json - the deepest name the program writes itself.
        var room = 259 - Path.GetFullPath(to).Length - "\\".Length - "\\scenes\\".Length - ".json".Length;

        return new MovePlan(campaigns, bytes, [.. campaigns.Where(taken.Contains)], room);
    }

    /// <summary>
    /// Moves every campaign. Throws <see cref="CampaignMoveException"/> - with nothing changed - when a
    /// campaign of the same name is at the target, when there is not room for a copy, or when the move
    /// fails or is broken off.
    /// </summary>
    /// <param name="rename">The rename, handed in so a test can refuse it as a volume boundary would.</param>
    /// <param name="copyFile">The copy of one file, handed in so a test can break a copy off half-way.</param>
    /// <param name="freeSpace">Free bytes at the target, handed in - a full disk is not something a test creates.</param>
    public static MoveResult Move(
        string from,
        string to,
        IProgress<(long Done, long Total)>? progress = null,
        Action<string, string>? rename = null,
        Action<string, string>? copyFile = null,
        Func<string, long>? freeSpace = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Plan(from, to);

        if (plan.Collisions.Count > 0)
        {
            throw new CampaignMoveException($"Campaigns of the same name are already at the new place: {string.Join(", ", plan.Collisions)}.");
        }

        if (plan.Campaigns.Count == 0)
        {
            return new MoveResult(Copied: false, []);
        }

        rename ??= Directory.Move;

        if (TryRename(from, to, plan, rename))
        {
            return new MoveResult(Copied: false, []);
        }

        var free = (freeSpace ?? FreeSpaceAt)(to);

        if (free < plan.Bytes + Reserve)
        {
            throw new CampaignMoveException(
                $"There is not room at the new place: the campaigns need {plan.Bytes / (1024d * 1024 * 1024):0.0} GB while they lie there twice, and {free / (1024d * 1024 * 1024):0.0} GB are free.");
        }

        Copy(from, to, plan, progress, copyFile ?? ((source, target) => File.Copy(source, target)), cancellationToken);

        return new MoveResult(Copied: true, [.. plan.Campaigns.Select(name => Path.Combine(from, name))]);
    }

    /// <summary>
    /// Takes the old copies away, after the new place is in use. What cannot be deleted right now - a
    /// file open in a viewer - stays and is named; the campaigns are complete at the new place either way.
    /// </summary>
    public static IReadOnlyList<string> RemoveSources(MoveResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var left = new List<string>();

        foreach (var source in result.SourcesToRemove)
        {
            try
            {
                Directory.Delete(source, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left.Add(source);
            }
        }

        return left;
    }

    /// <summary>
    /// The cheap way. A target that does not exist yet takes the whole base folder in one rename - the
    /// commonest case and the cheapest; otherwise each campaign is renamed into it. False when the
    /// system refused the first rename, which is the volume boundary answering.
    /// </summary>
    private static bool TryRename(string from, string to, MovePlan plan, Action<string, string> rename)
    {
        if (!Directory.Exists(to))
        {
            if (Path.GetDirectoryName(Path.GetFullPath(to)) is { } parent)
            {
                Directory.CreateDirectory(parent);
            }

            try
            {
                rename(from, to);

                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        var done = new List<string>();

        foreach (var name in plan.Campaigns)
        {
            try
            {
                rename(Path.Combine(from, name), Path.Combine(to, name));
                done.Add(name);
            }
            catch (IOException refused)
            {
                if (done.Count == 0)
                {
                    return false;
                }

                // Half renamed: put back what moved, then say so.
                foreach (var back in done)
                {
                    rename(Path.Combine(to, back), Path.Combine(from, back));
                }

                throw new CampaignMoveException($"The campaign \"{name}\" could not be moved: {refused.Message}", refused);
            }
        }

        return true;
    }

    private static void Copy(
        string from,
        string to,
        MovePlan plan,
        IProgress<(long Done, long Total)>? progress,
        Action<string, string> copyFile,
        CancellationToken cancellationToken)
    {
        var made = new List<string>();
        var done = 0L;

        try
        {
            Directory.CreateDirectory(to);

            foreach (var name in plan.Campaigns)
            {
                var source = Path.Combine(from, name);
                var target = Path.Combine(to, name);

                Directory.CreateDirectory(target);
                made.Add(target);

                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var copy = Path.Combine(target, Path.GetRelativePath(source, file));

                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    copyFile(file, copy);

                    done += new FileInfo(file).Length;
                    progress?.Report((done, plan.Bytes));
                }

                if (Count(source) != Count(target) || Size(source) != Size(target))
                {
                    throw new CampaignMoveException($"The copy of \"{name}\" does not match its source in number or size of files.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or CampaignMoveException)
        {
            foreach (var target in made)
            {
                try
                {
                    Directory.Delete(target, recursive: true);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    // A copy that will not go is a foreign folder at the new place, and nothing points at it.
                }
            }

            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw ex as CampaignMoveException ?? new CampaignMoveException($"The campaigns could not be copied: {ex.Message}", ex);
        }
    }

    private static long Size(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    private static int Count(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count();

    private static long FreeSpaceAt(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));

            return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }
}
