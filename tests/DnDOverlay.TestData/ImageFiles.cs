using System.Collections.Immutable;
using ImageMagick;

namespace DnDOverlay.TestData;

/// <summary>
/// The images written through Magick.NET - the half of the stock that is at the same time the
/// capability probe (Part 10).
/// <para>
/// Every file is the smallest thing that still carries its intent. What is checked is THAT a
/// format decodes, not what it looks like; a 64x64 AVIF proves the same as a 1024x1024 one and
/// costs a fraction. That is not thrift for its own sake - the stock is built on every
/// <c>dotnet test</c>, so its duration is a property of the daily work rather than a number in CI.
/// </para>
/// </summary>
internal static class ImageFiles
{
    /// <summary>The token portrait. The size IS the assertion in the unpacker tests (Part 11).</summary>
    internal const uint PortraitWidth = 400;

    /// <summary>The token portrait. The size IS the assertion in the unpacker tests (Part 11).</summary>
    internal const uint PortraitHeight = 600;

    /// <summary>The map token, taken only as the fallback when a portrait is missing.</summary>
    internal const uint MapTokenSide = 100;

    private const uint Side = 64;

    /// <summary>
    /// The formats that are tried but never promised (Part 5). JPEG XL and XCF are the two the
    /// plan names; measured on this build, only JXL can be WRITTEN - XCF has no encode delegate,
    /// so the choice between them is settled by measurement rather than preference.
    /// </summary>
    private static readonly (string Name, MagickFormat Format, string Extension)[] ToleratedCandidates =
    [
        ("JPEG XL", MagickFormat.Jxl, ".jxl"),
        ("XCF", MagickFormat.Xcf, ".xcf"),
    ];

    /// <summary>What one pass wrote, including the two images the token containers carry.</summary>
    internal sealed record Written(
        ImmutableDictionary<string, string> Promised,
        ImmutableDictionary<string, string> Tolerated,
        ImmutableArray<string> Skipped,
        string Portrait,
        string MapToken);

    internal static Written Write(string directory)
    {
        var promised = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        promised.Add("PNG", WritePngWithAlpha(directory));
        promised.Add("JPEG", WriteJpeg(directory));
        promised.Add("GIF", WriteAnimatedGif(directory));
        promised.Add("BMP", WriteSimple(directory, "plain.bmp", MagickFormat.Bmp));
        promised.Add("WebP", WriteSimple(directory, "still.webp", MagickFormat.WebP));
        promised.Add("AVIF", WriteSimple(directory, "still.avif", MagickFormat.Avif));

        // Not promised, but part of the positive list of Part 11 step 13 - and cheap here.
        WriteAnimatedWebP(directory);
        WriteSimple(directory, "scan.tiff", MagickFormat.Tiff);
        WriteSimple(directory, "layered.psd", MagickFormat.Psd);
        WritePanorama(directory);
        WriteJpegWithGpsExif(directory);
        var (portrait, mapToken) = WriteTokenImages(directory);

        var tolerated = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var skipped = ImmutableArray.CreateBuilder<string>();

        foreach (var (name, format, extension) in ToleratedCandidates)
        {
            var path = Path.Combine(directory, "tolerated" + extension);

            // A tolerated format may fall away without a word of complaint: nothing is asserted
            // about it anywhere, so nothing can branch on it (Part 5). The promised ones above
            // take the opposite route and throw out of WriteSimple with their name.
            try
            {
                using var image = Canvas(MagickColors.SteelBlue);
                image.Write(path, format);
                tolerated.Add(name, path);
            }
            catch (MagickException)
            {
                skipped.Add(name);
            }
        }

        return new Written(
            promised.ToImmutable(), tolerated.ToImmutable(), skipped.ToImmutable(), portrait, mapToken);
    }

    private static MagickImage Canvas(IMagickColor<byte> colour, uint width = Side, uint height = Side)
        => new(colour, width, height);

    private static string WriteSimple(string directory, string fileName, MagickFormat format)
    {
        var path = Path.Combine(directory, fileName);

        try
        {
            using var image = Canvas(MagickColors.SteelBlue);
            image.Write(path, format);
        }
        catch (MagickException ex)
        {
            throw Missing(format, ex);
        }

        return path;
    }

    /// <summary>PNG carries the alpha channel that decides the output format (Part 5).</summary>
    private static string WritePngWithAlpha(string directory)
    {
        var path = Path.Combine(directory, "alpha.png");

        try
        {
            using var image = Canvas(MagickColors.Transparent);
            using var opaque = Canvas(MagickColors.Firebrick, Side / 2, Side / 2);
            image.Composite(opaque, 16, 16, CompositeOperator.Over);
            image.Write(path, MagickFormat.Png);
        }
        catch (MagickException ex)
        {
            throw Missing(MagickFormat.Png, ex);
        }

        return path;
    }

    private static string WriteJpeg(string directory)
    {
        var path = Path.Combine(directory, "photo.jpg");

        try
        {
            using var image = Canvas(MagickColors.Goldenrod);
            image.Write(path, MagickFormat.Jpeg);
        }
        catch (MagickException ex)
        {
            throw Missing(MagickFormat.Jpeg, ex);
        }

        return path;
    }

    /// <summary>
    /// Animated, with frame times - both belong to the promise, and a still GIF would prove
    /// neither (Part 11).
    /// </summary>
    private static string WriteAnimatedGif(string directory)
    {
        var path = Path.Combine(directory, "animated.gif");

        try
        {
            using var frames = Frames();
            frames.Write(path, MagickFormat.Gif);
        }
        catch (MagickException ex)
        {
            throw Missing(MagickFormat.Gif, ex);
        }

        return path;
    }

    private static void WriteAnimatedWebP(string directory)
    {
        var path = Path.Combine(directory, "animated.webp");

        try
        {
            using var frames = Frames();
            frames.Write(path, MagickFormat.WebP);
        }
        catch (MagickException ex)
        {
            throw Missing(MagickFormat.WebP, ex);
        }
    }

    private static MagickImageCollection Frames()
    {
        var frames = new MagickImageCollection();

        foreach (var (colour, delay) in new[]
        {
            (MagickColors.Red, 10u), (MagickColors.Green, 20u), (MagickColors.Blue, 30u),
        })
        {
            var frame = Canvas(colour);
            frame.AnimationDelay = delay;
            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>10:1, for the width capping of Part 3.</summary>
    private static void WritePanorama(string directory)
    {
        using var image = Canvas(MagickColors.DarkSlateGray, Side * 10, Side);
        image.Write(Path.Combine(directory, "panorama.png"), MagickFormat.Png);
    }

    /// <summary>
    /// The counter-check that metadata is stripped: after the ingest this file must be byte-equal
    /// apart from the removed APPn segments, and the coordinates must be gone (Part 11 step 13).
    /// </summary>
    private static void WriteJpegWithGpsExif(string directory)
    {
        using var image = Canvas(MagickColors.Sienna);

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(52), new Rational(31), new Rational(12)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(13), new Rational(24), new Rational(36)]);
        image.SetProfile(exif);

        image.Write(Path.Combine(directory, "gps.jpg"), MagickFormat.Jpeg);
    }

    /// <summary>
    /// The two images the token containers carry. They are told apart by their MEASUREMENTS, never
    /// by a hash and never by a MapTool version - the token is built by us and may be rebuilt at
    /// any time without the assertion turning false (Part 5).
    /// </summary>
    private static (string Portrait, string MapToken) WriteTokenImages(string directory)
    {
        var portraitPath = Path.Combine(directory, "token-portrait.png");
        var mapPath = Path.Combine(directory, "token-map.png");

        using (var portrait = Canvas(MagickColors.MediumPurple, PortraitWidth, PortraitHeight))
        {
            portrait.Write(portraitPath, MagickFormat.Png);
        }

        using (var map = Canvas(MagickColors.OliveDrab, MapTokenSide, MapTokenSide))
        {
            map.Write(mapPath, MagickFormat.Png);
        }

        return (portraitPath, mapPath);
    }

    /// <summary>
    /// A promised format that cannot be written is a broken promise, and the run stops with its
    /// NAME in the message. Without that separation a routine dependency bump would shrink the
    /// promise green: the generator would leave WebP out, the parcours would stop checking WebP,
    /// everything would pass, and the README would go on promising it (Part 5, Part 10).
    /// </summary>
    private static InvalidOperationException Missing(MagickFormat format, MagickException inner)
        => new(
            $"This Magick build cannot write {format}, which is a promised format "
            + $"({string.Join(", ", TestAssets.MandatoryFormats)}). Either the package goes back, "
            + "or the format leaves the promise - and then it leaves the README too (Part 5).",
            inner);
}
