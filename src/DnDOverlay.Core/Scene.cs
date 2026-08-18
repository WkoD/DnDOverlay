using System.Text.Json.Serialization;

namespace DnDOverlay.Core;

/// <summary>
/// What is known about an image without decoding it - the thumbnail and the edge clamp compute
/// before the picture has arrived, and <see cref="IsAnimated"/> decides whether "pause
/// animation" is offered at all (Part 3).
/// </summary>
/// <param name="ContentHash">
/// SHA-256 of the DELIVERED bytes, separate from the <see cref="AssetId"/>, which hashes the
/// source. Identity and integrity are two questions: without the split the display could not
/// verify at all, because the file name carries the source identity (Part 5).
/// </param>
public sealed record AssetMeta(
    int PixelWidth,
    int PixelHeight,
    string Format,
    long Bytes,
    bool IsAnimated,
    string ContentHash)
{
    /// <summary>Width divided by height, the value that goes onto the item.</summary>
    public double AspectRatio => PixelHeight == 0 ? 0 : (double)PixelWidth / PixelHeight;
}

/// <summary>
/// One thing lying on a screen. A discriminated base, because text cards join later as a second
/// type - and the JSON discriminator is a FIXED list rather than a transmitted type name, which
/// is the security property, not an optimisation (Part 4).
/// </summary>
/// <param name="AspectRatio">
/// Sits on the item, not on <see cref="AssetMeta"/>: layout, edge clamp, flow placement and the
/// thumbnail need the shape of EVERY item, including a text card that has no asset (Part 3).
/// </param>
/// <param name="Revision">Handed out by the hub alone, which is what makes the order globally unambiguous.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ImageItem), "image")]
public abstract record SceneItem(
    ItemId ItemId,
    double CenterX,
    double CenterY,
    double Scale,
    double AspectRatio,
    double RotationDeg,
    int ZOrder,
    bool Locked,
    bool Parked,
    long Revision);

/// <summary>An image on a screen.</summary>
/// <param name="Name">
/// Belongs to the asset - one name per image in the campaign, and changing it in the inventory
/// changes it everywhere (Part 3). It is carried on the item so a display can draw the caption
/// without knowing the inventory.
/// </param>
/// <param name="ShowName">
/// Belongs to the INSTANCE: the same NPC may be captioned here and nameless over there.
/// </param>
public sealed record ImageItem(
    ItemId ItemId,
    double CenterX,
    double CenterY,
    double Scale,
    double AspectRatio,
    double RotationDeg,
    int ZOrder,
    bool Locked,
    bool Parked,
    long Revision,
    AssetId AssetId,
    AssetMeta Meta,
    string Name,
    bool ShowName,
    bool AnimationPaused)
    : SceneItem(ItemId, CenterX, CenterY, Scale, AspectRatio, RotationDeg, ZOrder, Locked, Parked, Revision);

/// <summary>
/// Where an item lies, as an INTENTION - what a display reports after a gesture and what the DM's
/// hand in the thumbnail will report from M4 on.
/// <para>
/// It carries no <c>ZOrder</c> and no <c>Revision</c>: both are handed out by the hub alone, and
/// a sender that could set them would be deciding the global order of a table it can only see
/// part of (Part 4).
/// </para>
/// </summary>
public sealed record ItemTransform(
    ItemId Item,
    double CenterX,
    double CenterY,
    double Scale,
    double RotationDeg);

/// <summary>How the background layer fills the screen.</summary>
public enum BackgroundFit
{
    /// <summary>Fills the area and crops - the default.</summary>
    Cover = 0,

    /// <summary>Shows everything with a margin; a panorama is unusable under <see cref="Cover"/>.</summary>
    Contain = 1,
}

/// <summary>
/// The background layer of a scene - the image the DM sets, never to be confused with the
/// UNDERLAY, which is whatever runs beneath our overlay on the display PC (Part 7).
/// </summary>
public sealed record BackgroundItem(
    AssetId AssetId,
    AssetMeta Meta,
    string? Name,
    bool ShowName,
    BackgroundFit Fit,
    double OffsetX,
    double OffsetY,
    bool AnimationPaused);

/// <summary>
/// The whole of one screen. The hub keeps one of these per <see cref="ScreenRef"/> - per screen,
/// not per device - and blackout is deliberately not in here: it is a screen state (Part 3).
/// <para>
/// Equality is structural over the collections. Records compare list members by reference, and
/// half a dozen promises in Part 11 are phrased as "twice the same result" or "field-identical";
/// with reference equality those tests would pass or fail depending on whether a list happened
/// to be reused.
/// </para>
/// </summary>
/// <param name="FocusItems">
/// Highlights one or more items without changing them - empty means no focus. It belongs to the
/// scene rather than to the control side because the display must know it, it has to survive a
/// reconnect, and the thumbnail shows it (Part 3).
/// </param>
public sealed record SceneState(
    BackgroundItem? Background,
    IReadOnlyList<SceneItem> Items,
    bool ItemsVisible,
    bool BackgroundVisible,
    IReadOnlyList<ItemId> FocusItems)
{
    /// <summary>A screen with nothing on it. Both layers visible, because that is the resting state.</summary>
    public static SceneState Empty { get; } =
        new(null, [], ItemsVisible: true, BackgroundVisible: true, []);

    /// <summary>The highest <c>ZOrder</c> in use, or -1 when the screen is empty.</summary>
    public int TopZOrder => Items.Count == 0 ? -1 : Items.Max(item => item.ZOrder);

    public bool Equals(SceneState? other) =>
        other is not null
        && Background == other.Background
        && ItemsVisible == other.ItemsVisible
        && BackgroundVisible == other.BackgroundVisible
        && Items.SequenceEqual(other.Items)
        && FocusItems.SequenceEqual(other.FocusItems);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Background);
        hash.Add(ItemsVisible);
        hash.Add(BackgroundVisible);

        foreach (var item in Items)
        {
            hash.Add(item);
        }

        foreach (var focus in FocusItems)
        {
            hash.Add(focus);
        }

        return hash.ToHashCode();
    }
}
