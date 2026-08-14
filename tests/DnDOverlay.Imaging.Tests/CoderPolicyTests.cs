using DnDOverlay.TestData;
using ImageMagick;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// That the policy is APPLIED proves nothing - a policy applied at the wrong moment silently has
/// no effect at all, which is exactly how it was measured to fail. These tests therefore ask
/// ImageMagick itself, through the same calls an attacker's file would take (Part 5, Part 11).
/// </summary>
public sealed class CoderPolicyTests(TestDataFixture fixture)
{
    private readonly TestAssetSet _assets = fixture.Assets;

    /// <summary>
    /// The decisive one: the policy has to bite even when our own format check is skipped. The
    /// test calls Magick directly and FORCES the format, so nothing but the policy can stop it.
    /// </summary>
    [Theory]
    [InlineData(MagickFormat.Mvg)]
    [InlineData(MagickFormat.Msl)]
    public void ScriptCodersAreRefusedEvenWhenTheFormatIsForced(MagickFormat format)
    {
        var script = File.ReadAllBytes(_assets.Crafted.ScriptDisguisedAsPng);

        Assert.Throws<MagickPolicyErrorException>(
            () => new MagickImage(script, new MagickReadSettings { Format = format }).Dispose());
    }

    /// <summary>
    /// The URL coders are the ones the import makes reachable from outside, so they are the ones
    /// worth a test of their own.
    /// </summary>
    [Fact]
    public void FetchingCodersAreRefused()
        => Assert.Throws<MagickPolicyErrorException>(
            () => new MagickImage("http://example.invalid/pixel.png").Dispose());

    /// <summary>
    /// A positive list that lets everything through would pass every test above by accident. This
    /// is the counter-check that it is a list at all: a raster coder deliberately left off it is
    /// refused, with the policy's own exception rather than a missing-format one.
    /// </summary>
    [Fact]
    public void ACoderLeftOffTheListIsRefused()
    {
        using var image = new MagickImage(_assets.Promised["PNG"]);

        // PostScript is a raster-capable coder this build carries; it is off the list because it
        // runs through Ghostscript (Part 5).
        Assert.Throws<MagickPolicyErrorException>(() => image.ToByteArray(MagickFormat.Ps));
    }

    /// <summary>
    /// And the counter-check to the counter-check: the list is not so narrow that the promise
    /// cannot travel through it. Every promised format reads back, in the process the policy is
    /// in force in.
    /// </summary>
    [Fact]
    public void EveryPromisedFormatSurvivesThePolicy()
    {
        Assert.Equal(TestAssets.MandatoryFormats.Length, _assets.Promised.Count);

        foreach (var name in TestAssets.MandatoryFormats)
        {
            using var image = new MagickImage(_assets.Promised[name]);
            Assert.True(image.Width > 0, $"{name} decoded to nothing");
        }
    }
}
