using System.IO;
using System.Windows.Media.Imaging;
using DnDOverlay.Imaging;
using DnDOverlay.TestData;

namespace DnDOverlay.Rendering.Windows.Tests;

/// <summary>
/// The seam between the two halves of the picture path: <see cref="MagickCodec"/> writes, and the
/// decoder the display really runs reads. Both are the real thing - no stand-in on either side.
/// <para>
/// Part 5 says the display only ever sees PNG, JPEG and GIF and that "WIC can do all three without
/// any help". That was an assumption for as long as this file did not exist: everything Imaging
/// produced had only ever been read back by Magick, which is the same library that wrote it.
/// </para>
/// <para>
/// What is checked is that the two AGREE - the picture arrives, and it arrives at the size the
/// codec said it had. "It did not throw" would be a poor promise: a decoder that silently produced
/// a 1×1 pixel would pass it.
/// </para>
/// </summary>
public sealed class CodecToWicSeamTests(TestDataFixture fixture)
{
    private readonly TestAssetSet _assets = fixture.Assets;
    private readonly MagickCodec _codec = new();

    /// <summary>
    /// Every source in the stock, normalised and then decoded by the display's own decoder. The
    /// sources are the broad end of the funnel; what comes out is one of three formats, and this
    /// is the check that the narrow end is one WIC actually reads.
    /// </summary>
    [Theory]
    [InlineData("alpha.png")]
    [InlineData("plain.bmp")]
    [InlineData("still.webp")]
    [InlineData("still.avif")]
    [InlineData("scan.tiff")]
    [InlineData("layered.psd")]
    [InlineData("animated.gif")]
    [InlineData("animated.webp")]
    [InlineData("photo.jpg")]
    public void WhatTheCodecWritesIsWhatTheDisplayReads(string fileName)
    {
        var normalised = _codec.Normalise(File.ReadAllBytes(Path.Combine(_assets.Directory, fileName)));

        var decoded = PictureDecoder.Decode(normalised.Bytes);

        // The size is the assertion, not the absence of an exception: both sides have to be
        // talking about the same picture.
        Assert.Equal(normalised.PixelWidth, decoded.PixelWidth);
        Assert.Equal(normalised.PixelHeight, decoded.PixelHeight);
    }

    /// <summary>
    /// The passed-through JPEG, and it is the reason this project exists. It is not re-encoded -
    /// it is the original file with segments cut out of it by our own byte surgery
    /// (<c>JpegSegments</c>: APP1, APP13 and COM removed, APP0, APP2 and APP14 kept), and until
    /// now the only reader that had ever seen the result was Magick.
    /// <para>
    /// APP14 is the one that would show here rather than in a Magick round trip: it carries
    /// Adobe's colour transform, and a JPEG that lost it decodes to inverted colours instead of
    /// failing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheJpegWePassThroughSurvivesOurOwnByteSurgery()
    {
        var source = File.ReadAllBytes(Path.Combine(_assets.Directory, "gps.jpg"));
        var normalised = _codec.Normalise(source);

        Assert.Equal("jpg", normalised.Format);
        Assert.True(normalised.Bytes.Length < source.Length, "nothing was cut out at all");

        var decoded = PictureDecoder.Decode(normalised.Bytes);

        Assert.Equal(normalised.PixelWidth, decoded.PixelWidth);
        Assert.Equal(normalised.PixelHeight, decoded.PixelHeight);
    }

    /// <summary>
    /// The other half of Part 5's assumption, and it does <b>not</b> hold: a
    /// <see cref="BitmapImage"/> reads exactly one frame of an animated GIF. The picture is right,
    /// it simply does not move.
    /// <para>
    /// Measured rather than argued, and stated as a test so that it is a finding rather than a
    /// footnote: the frames ARE in the file - <see cref="BitmapDecoder"/> counts all three of them
    /// - so what is missing is a decoder on our side, not data. That is M2b's animation work
    /// (Part 6), and this test is what will fail when it is done.
    /// </para>
    /// </summary>
    [Fact]
    public void AnAnimatedGifArrivesAsASingleFrameAndTheFramesAreThere()
    {
        var normalised = _codec.Normalise(
            File.ReadAllBytes(Path.Combine(_assets.Directory, "animated.gif")));

        Assert.True(normalised.IsAnimated);

        var decoded = PictureDecoder.Decode(normalised.Bytes);

        Assert.Equal(normalised.PixelWidth, decoded.PixelWidth);

        using var stream = new MemoryStream(normalised.Bytes);
        var frames = BitmapDecoder
            .Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
            .Frames;

        Assert.Equal(3, frames.Count);
    }

    /// <summary>
    /// Would these tests be green whatever came out of the codec? No - and this is the measurement
    /// that says so rather than the reasoning (Guide <c>G8</c>). Bytes that are not a picture reach
    /// the caller as a refusal instead of as a bitmap of nothing.
    /// </summary>
    [Fact]
    public void BytesThatAreNotAPictureAreRefused()
    {
        var rubbish = new byte[512];
        Random.Shared.NextBytes(rubbish);

        Assert.Throws<System.NotSupportedException>(() => PictureDecoder.Decode(rubbish));
    }
}
