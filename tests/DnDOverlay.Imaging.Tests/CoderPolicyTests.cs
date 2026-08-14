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
    /// The script coders are refused even when our own format check is skipped: the test calls
    /// Magick directly and FORCES the format, so no code of ours is in the way.
    /// <para>
    /// What this does NOT prove is that OUR policy is what refuses them. Measured while building
    /// the self-check in <see cref="CoderPolicy"/>: Magick.NET denies MVG and MSL in its own
    /// defaults already, so this test passes with our policy and without it. The end state is
    /// worth asserting - it is the guarantee Part 11 asks for - but the proof that our list is in
    /// force is <see cref="ACoderLeftOffTheListIsRefused"/>, which uses a coder the defaults allow.
    /// </para>
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
    /// The one test that proves OUR list is in force. A positive list that let everything through
    /// would pass the two above by accident, because Magick's own defaults already deny what they
    /// ask about.
    /// <para>
    /// PostScript is the counter-example that settles it: measured, this build writes 5766 bytes
    /// of it WITHOUT our policy, and refuses it with the policy's own exception once ours is in
    /// force. So a refusal here can only come from our list.
    /// </para>
    /// </summary>
    [Fact]
    public void ACoderLeftOffTheListIsRefused()
    {
        using var image = new MagickImage(_assets.Promised["PNG"]);

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
