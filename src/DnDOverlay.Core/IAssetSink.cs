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
    public sealed record Refused(ImageRejection Reason, string Detail) : IngestResult;
}
