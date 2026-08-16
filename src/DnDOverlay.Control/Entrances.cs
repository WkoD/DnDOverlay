using System.IO;
using System.Windows;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Transport;

namespace DnDOverlay.Control;

/// <summary>
/// The four ways a picture gets in (Part 5, Part 7): a file dropped or chosen, a screenshot pasted,
/// a browser paste, and an address fetched.
/// <para>
/// <b>What this class is:</b> the translation from a Windows <c>IDataObject</c> into sources the
/// intake can run, and nothing else. Every decision worth arguing about lives elsewhere on purpose -
/// the name in <see cref="AssetNaming"/>, the fetch in <see cref="PictureFetch"/>, the loop and the
/// report in <see cref="Intake"/>, the unpacking inside the stock. What is left here is the part
/// that can only exist on Windows, and it is deliberately the smallest part.
/// </para>
/// <para>
/// <b>A drop is read in the same format order as a paste</b>, and that is a rule rather than
/// symmetry (Part 7): built as a plain file drop, a link dragged out of a chat window or an address
/// bar falls through silently. And the drop layer filters no extension - it takes what it is given
/// and the ingest decides what it was, or else <c>.rptok</c> would be locked out by the very layer
/// that is supposed to let it in.
/// </para>
/// </summary>
internal sealed class Entrances(IAssetSink stock, ControlSettings settings)
{
    /// <summary>
    /// The clipboard's order of preference (Part 7). <c>"PNG"</c> comes before the device
    /// independent bitmap because it is the lossless one and the only one carrying transparency;
    /// taking <c>CF_DIB</c> first was the Java version's mistake and cost every browser paste its
    /// alpha channel.
    /// </summary>
    private const string LosslessBitmap = "PNG";

    private readonly Intake _intake = new(stock);

    /// <summary>
    /// Sources for files, whatever they turn out to hold - and for folders, because check step 29a
    /// drags a FOLDER of two hundred pictures in one go, and a list that quietly kept only the
    /// files would have taken in nothing at all.
    /// </summary>
    internal IReadOnlyList<IntakeSource> FromFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return [.. paths.SelectMany(Expand).Select(One)];
    }

    /// <summary>
    /// A path, as the files it stands for. A folder is walked to its bottom, and the price is
    /// named: dropping a folder can bring in more than the eye saw. It is the better half of the
    /// trade - a DM whose NPC pictures sit in subfolders would otherwise get half an import with no
    /// hint that anything was left out, and everything that does come in is reported, nameable and
    /// removable.
    /// </summary>
    private static IEnumerable<string> Expand(string path)
    {
        if (File.Exists(path))
        {
            return [path];
        }

        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private IntakeSource One(string path) =>
        new(
            Named(new NameSource { FileName = Path.GetFileName(path) }),
            async token => new IntakeBytes.Ready(
                await File.ReadAllBytesAsync(path, token).ConfigureAwait(false)));

    /// <summary>
    /// Sources for what the clipboard or a drop is offering, in the order of Part 7: files, then
    /// the lossless bitmap, then the device independent bitmap, then an address.
    /// </summary>
    internal IReadOnlyList<IntakeSource> FromDataObject(IDataObject data)
    {
        if (data is null)
        {
            return [];
        }

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            return FromFiles(files);
        }

        var offer = Offer(data);

        if (Bytes(data, LosslessBitmap) is { } lossless)
        {
            return [Immediate(offer, lossless)];
        }

        // The screenshot, and the one case that reaches stage 5 of the naming: there is genuinely
        // nothing in a device independent bitmap to derive a name from. Asked of the data object
        // rather than of the clipboard, so that a DROP of the same shape behaves identically.
        if (Bitmap(data) is { } bitmap)
        {
            return [Immediate(offer, bitmap)];
        }

        return Address(offer) is { } address ? [Fetched(offer, address)] : [];
    }

    /// <summary>Runs whatever came of an entrance. One picture or two hundred, the same way.</summary>
    internal Task<IntakeReport> TakeInAsync(
        IReadOnlyList<IntakeSource> sources,
        IProgress<IntakeProgress>? progress,
        CancellationToken cancellationToken) =>
        _intake.TakeInAsync(sources, progress, cancellationToken);

    /// <summary>
    /// What the clipboard has to say about a name: the HTML with its header and fragment, and any
    /// text lying beside it (Part 3). Read here, decided in <c>Core</c>.
    /// </summary>
    private static NameSource Offer(IDataObject data) =>
        new()
        {
            Html = Text(data, DataFormats.Html),
            Text = Text(data, DataFormats.UnicodeText) ?? Text(data, DataFormats.Text),
        };

    private static string? Text(IDataObject data, string format)
    {
        try
        {
            return data.GetDataPresent(format) ? data.GetData(format) as string : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or OutOfMemoryException)
        {
            // The clipboard belongs to whoever last wrote to it, and reading it is allowed to fail
            // at any moment. A paste that cannot be read is an empty paste, never a crash.
            return null;
        }
    }

    private static byte[]? Bytes(IDataObject data, string format)
    {
        try
        {
            return data.GetDataPresent(format) && data.GetData(format) is MemoryStream stream
                ? stream.ToArray()
                : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// A pasted or dropped bitmap, encoded as PNG - a screenshot arrives as pixels and the stock
    /// takes bytes. PNG rather than anything else because this is the last point at which the
    /// picture is lossless; what it becomes in the stock is the codec's decision (Part 5).
    /// </summary>
    private static byte[]? Bitmap(IDataObject data)
    {
        System.Windows.Media.Imaging.BitmapSource? source;

        try
        {
            source = data.GetDataPresent(DataFormats.Bitmap)
                ? data.GetData(DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource
                : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or OutOfMemoryException)
        {
            return null;
        }

        if (source is null)
        {
            return null;
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();

        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));

        using var buffer = new MemoryStream();

        encoder.Save(buffer);

        return buffer.ToArray();
    }

    /// <summary>An address to fetch, if what lies there is one at all.</summary>
    private static string? Address(NameSource offer)
    {
        var candidate = offer.Text?.Trim();

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? candidate
            : null;
    }

    private IntakeSource Immediate(NameSource offer, byte[] bytes) =>
        new(Named(offer), _ => ValueTask.FromResult<IntakeBytes>(new IntakeBytes.Ready(bytes)));

    /// <summary>
    /// A fetched address, as one more source in the same list. The fetch happens when its turn
    /// comes, so a dragged link and a dropped file behave the same in a run of many - and the whole
    /// of Part 5's guarding sits inside <see cref="PictureFetch"/> rather than being repeated here.
    /// </summary>
    private IntakeSource Fetched(NameSource offer, string address) =>
        new(
            Named(offer with { SourceUrl = address }),
            async token =>
            {
                using var fetch = new PictureFetch();

                return await fetch.FetchAsync(address, token).ConfigureAwait(false) switch
                {
                    FetchResult.Fetched got => new IntakeBytes.Ready(got.Bytes),
                    FetchResult.Refused refused => new IntakeBytes.Unavailable(refused.Detail),
                    _ => new IntakeBytes.Unavailable("Nothing came back."),
                };
            });

    /// <summary>
    /// The derived name, and the counter kept where it survives a restart. Beginning again at one
    /// each session would collide with every name from the last, and the DM would be looking at
    /// "Clipboard 1 (2)" without having done anything twice.
    /// </summary>
    private string Named(NameSource offer)
    {
        var current = settings.Current;
        var name = AssetNaming.Derive(offer, current.CountedNamePattern, current.CountedNameNext);

        // Only a name that actually fell THROUGH to the counter advances it, asked by deriving the
        // same counted name from an empty offer: a folder of two hundred named files must not run
        // the counter up for nothing. A file that happens to be called exactly what the counter
        // would have produced advances it by one, which costs a number and nothing else.
        if (name == AssetNaming.Derive(new NameSource(), current.CountedNamePattern, current.CountedNameNext))
        {
            settings.Update(document => document with { CountedNameNext = current.CountedNameNext + 1 });
        }

        return name;
    }
}
