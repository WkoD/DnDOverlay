namespace DnDOverlay.Core;

/// <summary>
/// The way IN to the stock, the counterpart to <see cref="IAssetSource"/>. Every entrance - file
/// drop, screenshot paste, browser paste, URL import, token container - ends here, and each one
/// produces a stock entry (Part 5, Part 7).
/// <para>
/// One path for one file as for two hundred: the caller loops, the report is collected, and there
/// is no second, quicker way in that skips a check.
/// </para>
/// </summary>
public interface IAssetSink
{
    /// <summary>
    /// Takes source bytes into the stock and returns what the hub needs to make an item of them.
    /// <para>
    /// The bytes are the image AS IT CAME IN - after unpacking a container, before normalising.
    /// That is what "source" means for the <see cref="AssetId"/>, and it is why the same portrait
    /// out of two different token files is ONE entry (Part 5).
    /// </para>
    /// </summary>
    /// <param name="source">The image bytes.</param>
    /// <param name="proposedName">
    /// The name the entry should carry, from the five-stage derivation (Part 3). Taken as a
    /// proposal: a name already in use is numbered rather than refused, because an import of two
    /// hundred files must not stop to ask.
    /// </param>
    Task<IngestResult> IngestAsync(
        ReadOnlyMemory<byte> source, string proposedName, CancellationToken cancellationToken = default);
}

/// <summary>What became of one ingest.</summary>
public abstract record IngestResult
{
    private IngestResult()
    {
    }

    /// <summary>Taken in, or found to be there already.</summary>
    /// <param name="Asset">Identifier, measurements and name - all the hub needs (Part 2).</param>
    /// <param name="AlreadyPresent">
    /// The image was already in the stock and nothing was written. The DM is told, and the name
    /// they gave it earlier STAYS - re-importing a file must not rename it (Part 7).
    /// </param>
    /// <param name="Standing">
    /// Whether the source format was promised or merely tolerated. Carried so the collected
    /// message can say "1 x JPEG XL - worked, is not assured".
    /// </param>
    public sealed record Taken(AssetRef Asset, bool AlreadyPresent, FormatStanding Standing) : IngestResult;

    /// <summary>Refused, with the reason stated - never a silent swallow (Part 5).</summary>
    public sealed record Refused(IntakeRejection Reason, string Detail) : IngestResult;
}

/// <summary>
/// Why a file did not come in, in the ONE vocabulary the DM is answered in.
/// <para>
/// It exists because there are three of them inside. A picture is refused with an
/// <see cref="ImageRejection"/>, an address with a <c>FetchRejection</c>, and a file that is locked
/// or gone with neither - and until the M2c hand-run all three arrived in the log as
/// <c>Unreadable</c>, because that was the only word the picture's vocabulary had for them. Every
/// one of the sixty-three refusals of that evening said "unreadable file", including the pixel
/// bomb, the six hundred frames, the HEIC, every refused address and a <c>403</c>. The detail text
/// was right each time; the field one filters a log by was wrong.
/// </para>
/// <para>
/// So the mapping happens at the ENTRANCE, where both vocabularies are still in reach, and what
/// travels on is this. Two languages inside, one outside - and the outside one is chosen for the
/// question actually being asked at the table, which is "why did this file not come in", never
/// "which part of the program said no" (Part 5, Part 8).
/// </para>
/// </summary>
public enum IntakeRejection
{
    /// <summary>Not an image at all, an image this build cannot read, or an answer that was none.</summary>
    Unreadable,

    /// <summary>
    /// Refused although it may well work - HEIC/HEIF, and for legal rather than technical reasons.
    /// Its own entry for the same reason it has one in <see cref="ImageRejection"/>: folded into
    /// "unreadable" it would come in by accident the day a build can read it.
    /// </summary>
    NotPermitted,

    /// <summary>Beyond a limit - bytes, measurements or frame count.</summary>
    TooLarge,

    /// <summary>The decode was cut off by the resource limits: the second net, for a header that lied.</summary>
    Aborted,

    /// <summary>There is no room on the campaign's drive.</summary>
    NoSpace,

    /// <summary>
    /// The address itself is refused - a scheme that is not <c>http(s)</c>, or one that points
    /// inside the house. The one reason where "unreadable" was not merely imprecise but wrong: it
    /// is the answer of a security check, and it belongs in the log as one (Part 4).
    /// </summary>
    Address,

    /// <summary>Nothing answered, not in time, or not with a picture.</summary>
    Unreachable,

    /// <summary>The bytes could not be got at THIS end - a file locked, or gone since it was named.</summary>
    Unavailable,
}

/// <summary>Turning the inside vocabularies into the one the DM reads.</summary>
public static class IntakeRejections
{
    /// <summary>
    /// The picture's vocabulary. One to one, because every one of its reasons is about the file the
    /// DM handed over - the mapping exists so that the two enumerations can move apart later
    /// without the log line changing meaning.
    /// </summary>
    public static IntakeRejection AsIntake(this ImageRejection reason) => reason switch
    {
        ImageRejection.NotPermitted => IntakeRejection.NotPermitted,
        ImageRejection.TooLarge => IntakeRejection.TooLarge,
        ImageRejection.Aborted => IntakeRejection.Aborted,
        _ => IntakeRejection.Unreadable,
    };
}
