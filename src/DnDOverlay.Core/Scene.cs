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
    long Revision,

    /// <summary>
    /// When this item went into the fan, as a number the hub hands out - <c>0</c> while it is not
    /// parked. It orders the fan, and nothing else reads it.
    /// <para>
    /// A field of its own rather than the revision it happens to share today: a parked item's
    /// revision is the parking only for as long as nothing else ever touches a parked item, and
    /// the thumbnail of M4 will be able to. A number that answers two questions eventually answers
    /// the wrong one.
    /// </para>
    /// </summary>
    long ParkedAt = 0);

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
    bool AnimationPaused,
    long ParkedAt = 0)
    : SceneItem(
        ItemId, CenterX, CenterY, Scale, AspectRatio, RotationDeg, ZOrder, Locked, Parked, Revision, ParkedAt);

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

/// <summary>
/// How a background is laid out when it is set or when the DM asks for it - <b>two starting
/// values, not a stored property</b>.
/// <para>
/// Until M4 this sat in <see cref="BackgroundItem"/> and decided, together with an offset, what was
/// seen. The pair was a hybrid: the fit fixed the size, the offset moved inside the crop, and
/// between them they could not describe a section that a free scale describes in one number. So the
/// background now carries place and size like any picture, and these two became the buttons that
/// compute them (Part 6, decided at the start of M4).
/// </para>
/// </summary>
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
/// <para>
/// <b>It carries place and size like any other picture</b> - the same normalised centre, the same
/// height-relative scale, the same angle - so <see cref="Layout"/> computes both layers with one
/// formula (rule 9). What it does NOT carry are the four administrative fields of a
/// <see cref="SceneItem"/>: an <c>ItemId</c>, a <c>ZOrder</c>, <c>Locked</c> and <c>Parked</c>. The
/// background is a layer with exactly one place on it, it takes no hits at the table, and the scene
/// keeps the revision - inherited, each of them would be a number answering a second question it
/// was never asked (Guide C5).
/// </para>
/// <para>
/// <b>Its shape comes from <see cref="AssetMeta"/> and not from a field of its own</b>, the one
/// place it parts company with <see cref="ImageItem"/>. Part 3 gives the reason for the field
/// there - an item may have no asset at all - and a background always has one.
/// </para>
/// <para>
/// <b><see cref="RotationDeg"/> has no caller until the end of M4</b>, and that is written down
/// rather than discovered later: the fit buttons set centre and scale, the thumbnail's grips arrive
/// in M4c, and how a background is turned, zoomed and moved is decided when they do. At the table
/// it stays unreachable in any case - the background layer takes no gestures (Part 6).
/// </para>
/// </summary>
public sealed record BackgroundItem(
    AssetId AssetId,
    AssetMeta Meta,
    string? Name,
    bool ShowName,
    double CenterX,
    double CenterY,
    double Scale,
    double RotationDeg,
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
