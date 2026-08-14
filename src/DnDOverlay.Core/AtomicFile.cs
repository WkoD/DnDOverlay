namespace DnDOverlay.Core;

/// <summary>
/// Replacing a file without ever leaving half of one behind. The rule lives here once, because
/// "atomic" is otherwise a word every call site redeems its own way - and the difference would
/// never show on a development machine (Part 6).
/// <para>
/// Write beside it, then rename. <c>File.Move</c> maps onto a single rename call of the operating
/// system and is atomic everywhere; <c>File.Replace</c> is built around Windows semantics, carries
/// a backup copy and does not give the same guarantee elsewhere.
/// </para>
/// </summary>
public static class AtomicFile
{
    /// <summary>The suffix of the file that exists only between writing and renaming.</summary>
    public const string TemporarySuffix = ".tmp";

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/>, creating the directory if
    /// needed. Either the old file is there or the new one - never a truncated one.
    /// <para>
    /// The temporary carries a caller-supplied <paramref name="discriminator"/> so that two
    /// writers of the SAME target do not share one scratch file. Without it, two simultaneous
    /// ingests of the same image would write over each other's temporary and the loser could
    /// rename a half-written file into place under a valid hash (Part 11).
    /// </para>
    /// </summary>
    public static void Write(string path, ReadOnlySpan<byte> content, string? discriminator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + (discriminator is null ? string.Empty : "." + discriminator) + TemporarySuffix;

        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // A failure must not leave the scratch file lying around - the stock is checked for
            // exactly that ("no .tmp leftovers", Part 11).
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>
    /// The same, for a file whose NAME IS ITS CONTENT - the hash-addressed images of the stock.
    /// There, losing the race is success: two writers of one name are writing the same bytes by
    /// construction, so a destination that already exists is the file we were about to write.
    /// <para>
    /// Measured, not foreseen: eight simultaneous ingests of one image made two threads rename
    /// onto the same target, and the loser came back with "access to the path is denied". A plain
    /// existence check beforehand does not fix it - both can pass it - so the answer has to sit on
    /// the failure rather than in front of it.
    /// </para>
    /// <para>
    /// <b>Only for content-addressed files.</b> For configuration the opposite holds: there the
    /// last writer must win, because the name says nothing about what is inside.
    /// </para>
    /// </summary>
    public static void WriteContentAddressed(string path, ReadOnlySpan<byte> content, string? discriminator = null)
    {
        try
        {
            Write(path, content, discriminator);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && File.Exists(path))
        {
            // Somebody else put the identical bytes there first. Nothing to repair.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Nothing better to do here, and the original failure is the one worth reporting.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
