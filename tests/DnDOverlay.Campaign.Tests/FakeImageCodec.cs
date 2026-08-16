using System.Text;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// The codec the store is tested against (Part 11). A fake rather than the real one, and not to be
/// quick about it: the store's subject is identity, deduplication, atomic writing and the
/// inventory, and every one of those is easier to state wrongly when a real encoder's output is in
/// the way.
/// <para>
/// It earns one property no real encoder has, and the hash separation NEEDS it: this fake produces
/// output that is deliberately NOT byte-stable, so a test can show that the same source keeps its
/// <see cref="AssetId"/> while the <c>ContentHash</c> moves. Against a real encoder that case
/// cannot be staged at all - it is exactly the encoder update the split exists for (Part 5).
/// </para>
/// </summary>
internal sealed class FakeImageCodec : IImageCodec
{
    // A per-INSTANCE salt, because that is what the split guards against: a new encoder
    // version producing different bytes for the same source. Without it two fresh fakes agree,
    // and the very case the ContentHash exists for cannot be staged.
    private readonly string _salt = Guid.NewGuid().ToString("N");

    private int _writes;

    /// <summary>
    /// Bytes this fake always refuses, whatever the flags say. The flag refuses EVERY picture,
    /// which cannot stage the case a collected report is built for: three broken files among two
    /// hundred good ones (Part 7).
    /// </summary>
    internal static byte[] Unreadable { get; } = Encoding.UTF8.GetBytes("!! not a picture !!");

    /// <summary>Set to refuse the next probe, to stage a rejection with a stated reason.</summary>
    internal ImageRejection? RefuseWith { get; set; }

    /// <summary>What the header claims. The store must believe it and apply the limits to it.</summary>
    internal ImageProbe Claims { get; set; } = new("png", 64, 64, 1);

    /// <summary>Set to make thumbnail generation fail, which must not lose the image.</summary>
    internal bool ThumbnailFails { get; set; }

    /// <summary>How many times bytes were normalised - the counter that shows dedup did its job.</summary>
    internal int Normalisations { get; private set; }

    /// <summary>Whether the source format counts as promised or as merely tolerated.</summary>
    internal FormatStanding Standing { get; set; } = FormatStanding.Promised;

    public ImageProbe Probe(ReadOnlyMemory<byte> source)
    {
        if (RefuseWith is { } reason)
        {
            throw new ImageRejectedException(reason, "staged refusal");
        }

        if (source.Span.StartsWith(Unreadable))
        {
            throw new ImageRejectedException(ImageRejection.Unreadable, "this one is not a picture");
        }

        return Claims;
    }

    public NormalisedImage Normalise(ReadOnlyMemory<byte> source)
    {
        if (RefuseWith is { } reason)
        {
            throw new ImageRejectedException(reason, "staged refusal");
        }

        Normalisations++;

        // Not byte-stable on purpose - see the type's remarks.
        var body = Encoding.UTF8.GetBytes($"normalised-{_salt}-{Interlocked.Increment(ref _writes)}-");

        return new NormalisedImage(
            [.. body, .. source.Span],
            "png",
            Claims.PixelWidth,
            Claims.PixelHeight,
            Claims.Frames > 1,
            Claims.Format,
            Standing);
    }

    public byte[] Thumbnail(ReadOnlyMemory<byte> delivered, int width)
    {
        if (ThumbnailFails)
        {
            throw new ImageRejectedException(ImageRejection.Unreadable, "staged thumbnail failure");
        }

        return Encoding.UTF8.GetBytes($"thumb-{width}");
    }
}
