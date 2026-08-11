namespace DnDOverlay.Core;

/// <summary>
/// Everything the hub needs to make an item out of an image: the identifier, the measurements
/// and the name.
/// <para>
/// It exists because the hub does NOT own the stock (Part 2) and therefore cannot look any of
/// this up. Handing it the triple instead of a bare <see cref="AssetId"/> is not a convenience -
/// it is what keeps the campaign folder out of the hub. The same three values sit on every item
/// of every saved scene anyway, so this is not a new concept, only a named one (Part 4).
/// </para>
/// </summary>
public sealed record AssetRef(AssetId AssetId, AssetMeta Meta, string Name);
