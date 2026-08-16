namespace DnDOverlay.Core;

/// <summary>
/// Unpacks a container that HOLDS a picture rather than being one - today a MapTool
/// <c>.rptok</c>, and later the related MapTool formats by the same route (Part 5).
/// <para>
/// <b>Defined here for the same reason <see cref="IImageCodec"/> is</b> (rule 8): the unpacking
/// lives in <c>Imaging</c>, and <c>Campaign</c> may not know that project. It is handed in, the
/// stock asks it, and a build without one simply has no containers.
/// </para>
/// <para>
/// <b>And it is asked inside the ingest, not at an entrance.</b> A container may arrive through any
/// of the four - dropped, pasted, fetched - and an entrance that forgot to unpack would put the ZIP
/// itself in the stock. One way in means one place that unpacks (see <see cref="IAssetSink"/>).
/// </para>
/// </summary>
public interface IContainerReader
{
    /// <summary>
    /// Whether these bytes are a container this reader can open, decided on CONTENT and never on a
    /// file extension - the extension can lie in both directions (Part 5).
    /// </summary>
    bool Holds(ReadOnlyMemory<byte> data);

    /// <summary>Opens it and hands out the picture the DM meant, with the name it carried.</summary>
    /// <exception cref="ImageRejectedException">
    /// Not a container, holding no usable picture, or beyond a limit - stated with a reason, never a
    /// wrong picture and never a crash.
    /// </exception>
    ContainerContent Read(ReadOnlyMemory<byte> data);
}

/// <summary>What came out of a container.</summary>
/// <param name="Name">
/// The name the container carried - a token's own "Testfigur" rather than an MD5 file name. This is
/// stage 1 of the five in Part 3, and it is the one stage no entrance can supply, because only the
/// unpacking sees it.
/// </param>
/// <param name="Image">
/// The picture bytes as they lay inside, before normalising. That is what "source" means for the
/// <see cref="AssetId"/>, and it is why the same portrait out of two different tokens - and the
/// same picture once as a token and once as a PNG - is ONE entry (Part 5).
/// </param>
public sealed record ContainerContent(string Name, byte[] Image);
