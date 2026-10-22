using DnDOverlay.Core;

namespace DnDOverlay.Campaign;

/// <summary>
/// The stock of whatever campaign is open, as the hub and the entrances see it (Part 3).
/// <para>
/// <b>The hub knows one source for the whole run</b> - it is registered once and never learns that
/// campaigns exist. Behind it the open campaign changes; with none open, nothing is served.
/// </para>
/// <para>
/// <b>A picture that comes in while none is open makes one</b>: the first ingest without an open
/// campaign creates a new unnamed one and opens it (Part 3). The folder exists from the first file
/// on, so no evening is lost because no name had been given yet.
/// </para>
/// </summary>
/// <param name="ensure">Opens a new unnamed campaign and answers with its stock - called by the first picture.</param>
public sealed class OpenStock(Func<AssetStore> ensure) : IAssetSource, IAssetSink
{
    private volatile AssetStore? _current;

    /// <summary>The open campaign's stock, or <see langword="null"/> while none is open.</summary>
    public AssetStore? Current
    {
        get => _current;
        set => _current = value;
    }

    /// <inheritdoc />
    public bool TryOpen(AssetId id, out Stream data, out string contentType)
    {
        if (_current is { } store)
        {
            return store.TryOpen(id, out data, out contentType);
        }

        data = Stream.Null;
        contentType = string.Empty;

        return false;
    }

    /// <inheritdoc />
    public bool TryOpenThumb(AssetId id, int width, out Stream data, out string contentType)
    {
        if (_current is { } store)
        {
            return store.TryOpenThumb(id, width, out data, out contentType);
        }

        data = Stream.Null;
        contentType = string.Empty;

        return false;
    }

    /// <inheritdoc />
    public Task<IngestResult> IngestAsync(
        ReadOnlyMemory<byte> source, string proposedName, CancellationToken cancellationToken = default)
    {
        AssetStore store;

        try
        {
            store = _current ?? ensure();
        }
        catch (CampaignBaseUnreachableException unreachable)
        {
            // Nothing is created anywhere instead (Part 3): the picture is refused, with the folder named.
            return Task.FromResult<IngestResult>(new IngestResult.Refused(IntakeRejection.Unavailable, unreachable.Message));
        }

        return store.IngestAsync(source, proposedName, cancellationToken);
    }
}
