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
/// The thumbnail half (<c>TryOpenThumb</c>) and the upload direction (<c>IAssetSink</c>) join in
/// the milestones that serve them - M2 and M8.
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
}
