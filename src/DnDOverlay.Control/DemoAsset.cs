using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DnDOverlay.Core;
using WpfRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// The one hard-coded asset M1a needs, and the smallest thing that can stand in for the campaign
/// stock: it is generated, hashed and served, so the running thread can be walked end to end
/// before Campaign and Imaging exist (M2).
/// <para>
/// It is drawn rather than shipped as a file, for the same reason the test data is generated
/// rather than obtained: in a PUBLIC repository a checked-in image is a publication, and then
/// somebody has to answer for its rights (Part 10).
/// </para>
/// </summary>
internal sealed class DemoAsset : IAssetSource
{
    private readonly byte[] _png;

    private DemoAsset(byte[] png, AssetRef reference)
    {
        _png = png;
        Reference = reference;
    }

    internal AssetRef Reference { get; }

    /// <summary>
    /// No thumbnail. The stand-in exists so the running thread could be walked before Campaign and
    /// Imaging did; a preview belongs to the real stock, and this type is due to disappear with it
    /// (M2, checks/M2.md).
    /// </summary>
    public bool TryOpenThumb(AssetId id, int width, out Stream data, out string contentType)
    {
        data = Stream.Null;
        contentType = string.Empty;

        return false;
    }

    /// <inheritdoc />
    public bool TryOpen(AssetId id, out Stream data, out string contentType)
    {
        if (id != Reference.AssetId)
        {
            data = Stream.Null;
            contentType = string.Empty;
            return false;
        }

        data = new MemoryStream(_png, writable: false);
        contentType = "image/png";

        return true;
    }

    /// <summary>Draws a plate that is recognisable from across a room and encodes it as PNG.</summary>
    internal static DemoAsset Create(int width = 1200, int height = 800)
    {
        var png = Draw(width, height);

        // The AssetId hashes the SOURCE bytes - what came in, not what goes out. Here the two
        // are the same because nothing is normalised yet; the split matters from M2 on, when
        // Imaging re-encodes and the delivered bytes get their own ContentHash (Part 5).
        var hash = Convert.ToHexStringLower(SHA256.HashData(png));

        var meta = new AssetMeta(
            width,
            height,
            "png",
            png.Length,
            IsAnimated: false,
            ContentHash: hash);

        return new DemoAsset(png, new AssetRef(new AssetId(hash), meta, "Grimmbart"));
    }

    private static byte[] Draw(int width, int height)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            var area = new WpfRect(0, 0, width, height);

            context.DrawRectangle(
                new LinearGradientBrush(
                    Color.FromRgb(0x1B, 0x2A, 0x41),
                    Color.FromRgb(0x5B, 0x21, 0x1B),
                    angle: 45),
                pen: null,
                area);

            context.DrawRoundedRectangle(
                brush: null,
                new Pen(Brushes.Goldenrod, 12),
                new WpfRect(30, 30, width - 60, height - 60),
                24,
                24);

            var text = new FormattedText(
                "DnDOverlay",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"),
                emSize: height / 8d,
                Brushes.Goldenrod,
                pixelsPerDip: 1);

            context.DrawText(text, new System.Windows.Point((width - text.Width) / 2, (height - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }
}
