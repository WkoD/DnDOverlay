using System.IO;
using System.Windows.Media.Imaging;

namespace DnDOverlay.Rendering.Windows;

/// <summary>
/// Turns the bytes that came off the wire into a frozen bitmap. WIC does the work, by way of
/// <see cref="BitmapImage"/>.
/// <para>
/// Decoding happens in the application rather than in Transport, and that has a cost behind it: a
/// decoded bitmap is uncompressed memory, width × height × 4 bytes, and the file size says nothing
/// about it - a 6000×4000 photo is 96 MB in memory even when the JPEG weighs 5 MB (Part 6).
/// </para>
/// </summary>
public static class PictureDecoder
{
    /// <summary>
    /// Decodes one picture. The result is <b>frozen</b>, which is what lets it cross to the UI
    /// thread and be shared between several items without a second copy.
    /// </summary>
    /// <exception cref="System.NotSupportedException">
    /// The bytes are not a picture this machine can read - the type WIC answers with, measured
    /// rather than assumed.
    /// <para>
    /// It is thrown rather than swallowed, because a decoder that answers <see langword="null"/>
    /// decides by omission what the caller should decide. <b>That puts an obligation on the
    /// caller</b>, and it is one this repository has already failed once: an exception out of the
    /// display's message loop ends the loop and takes the connection with it, without a word.
    /// The caller catches it.
    /// </para>
    /// </exception>
    /// <param name="pixelWidth">
    /// The step to decode at, in physical pixels, or <c>0</c> for the picture as it is
    /// (<see cref="DecodeSteps"/>).
    /// <para>
    /// WIC scales while it decodes, so the pixels above the step are never built in the first
    /// place - that is the difference between a step and a resize, and it is the whole memory
    /// saving. <b>Never larger than the source:</b> asking for more pixels than there are adds no
    /// detail and spends the memory twice, which is why the step is capped before it gets here.
    /// </para>
    /// </param>
    public static BitmapSource Decode(byte[] bytes, int pixelWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using var stream = new MemoryStream(bytes);

        var image = new BitmapImage();

        image.BeginInit();

        // OnLoad, because the stream is gone the moment this returns. Without it the bitmap keeps
        // a lazy hold on a disposed MemoryStream and fails at the first draw instead of here.
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;

        if (pixelWidth > 0)
        {
            // Width alone: the height follows the aspect ratio, and setting both would let a
            // rounding difference between the two squash the picture.
            image.DecodePixelWidth = pixelWidth;
        }

        image.EndInit();

        image.Freeze();

        return image;
    }
}
