using DnDOverlay.Transport;

namespace DnDOverlay.Display;

/// <summary>
/// The order arrived pictures are decoded in. <b>Thumbnails jump the queue; pictures keep their
/// order.</b> Two rules, and they are about different things - the first is about kind, the second
/// about time. Neither is about size.
/// <para>
/// Part 5 promises a picture STANDS at its place blurred within a second and is replaced when the
/// original lands, and that promise is kept by the order things are DECODED in, not the order they
/// arrive in. With three fetched at a time the two come apart: a full decode costs one to two and a
/// half seconds on the table's machine, and every thumbnail landing during it waited behind it - the
/// runner saw rings filling over nothing where a blurred picture belonged.
/// </para>
/// <para>
/// Among the originals the order is <b>the order they came in</b> and nothing else. <i>"The DM has
/// already thought about the order when he sends them"</i> - sorting them by anything of ours, size
/// above all, would quietly overrule a decision somebody made on purpose.
/// </para>
/// <para>
/// <b>A blur nobody has to wait for is not worth showing.</b> When a picture comes out of the store
/// rather than off the wire, its thumbnail and its original land in the same instant - and taking all
/// the thumbnails first then put twenty blurred pictures on the table at once, followed by twenty
/// sharpenings. So a thumbnail whose original is already in hand is dropped, and the pictures come up
/// sharp, one after another, in the order they arrived.
/// </para>
/// </summary>
internal sealed class DecodeOrder
{
    private readonly Queue<AssetArrived> _thumbnails = new();
    private readonly Queue<AssetArrived> _pictures = new();

    /// <summary>Whether nothing is waiting to be decoded.</summary>
    internal bool IsEmpty => _thumbnails.Count == 0 && _pictures.Count == 0;

    /// <summary>One arrival, taken off the channel as fast as it comes.</summary>
    internal void Add(AssetArrived arrived)
    {
        ArgumentNullException.ThrowIfNull(arrived);

        (arrived.IsThumbnail ? _thumbnails : _pictures).Enqueue(arrived);
    }

    /// <summary>
    /// Takes the next arrival in line, or <see langword="null"/> when what was in line was a thumbnail
    /// whose original is already in hand - dropped. The caller looks at the channel again before it
    /// asks once more, so a thumbnail landing meanwhile still goes first.
    /// </summary>
    internal AssetArrived? Take()
    {
        var next = _thumbnails.Count > 0 ? _thumbnails.Dequeue() : _pictures.Dequeue();

        return next.IsThumbnail && _pictures.Any(picture => picture.Asset == next.Asset) ? null : next;
    }
}
