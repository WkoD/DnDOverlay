namespace DnDOverlay.Core;

/// <summary>
/// The only thing the hub needs from the stock: a resolution from <see cref="AssetId"/> to
/// bytes, for <c>GET /assets/{id}</c>.
/// <para>
/// This is the whole of the line between the two (Part 2, Part 3): the ARRANGEMENT is the hub's,
/// the MATERIAL is the campaign's. The hub does not know the campaign folder, does not know the
/// file layout and cannot enumerate anything - it asks for one identifier and gets a stream or
/// nothing. A store it is not.
/// </para>
/// <para>
/// It lives in Core for the same reason <c>IImageCodec</c> does: Campaign knows only Core, so it
/// could not implement an interface declared anywhere else, and the architecture test asserts
/// that the hub never reaches Campaign directly.
/// </para>
/// <para>
/// The upload direction is <see cref="IAssetSink"/>, and it joins in M8 what this one does in M2.
/// </para>
/// </summary>
public interface IAssetSource
{
    /// <summary>
    /// Opens the delivered bytes of an asset, or returns <see langword="false"/> when this
    /// identifier resolves to nothing - which is the normal answer while no campaign is open
    /// (Part 5). The caller owns the stream.
    /// </summary>
    bool TryOpen(AssetId id, out Stream data, out string contentType);

    /// <summary>
    /// Opens a thumbnail, which is what makes a picture STAND at its place inside a second even
    /// when the full one is still coming (Part 5, Part 10).
    /// <para>
    /// <paramref name="width"/> is a wish rather than a demand: thumbnails are made once and kept,
    /// so what comes back is the step the stock holds. Serving a different one is right - the
    /// caller wants something to show, not a particular number of pixels - and generating on
    /// demand would put the most noticeable delay of all exactly where the promise of a second
    /// lives.
    /// </para>
    /// </summary>
    bool TryOpenThumb(AssetId id, int width, out Stream data, out string contentType);
}
