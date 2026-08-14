using DnDOverlay.TestData;

namespace DnDOverlay.Imaging.Tests;

/// <summary>
/// The guard against the routine dependency bump that would shrink the promise GREEN: the
/// generator would leave WebP out, the parcours would stop checking WebP, everything would pass,
/// and the README would go on promising it (Part 5, Part 10).
/// <para>
/// It was the one guard in the whole stock that had never fired. It could not: withdrawing a
/// format is a capability the generator did not have, so the check had no way to reach its own
/// failure - exactly the state <c>C2</c> is written against.
/// </para>
/// </summary>
public sealed class PromiseCannotShrinkTests
{
    /// <summary>
    /// Each promised format in turn, because a guard that catches one of six is not a guard. The
    /// message has to carry the NAME - "a format is missing" would leave whoever reads the failing
    /// build to find out which.
    /// </summary>
    [Theory]
    [InlineData("PNG")]
    [InlineData("JPEG")]
    [InlineData("GIF")]
    [InlineData("BMP")]
    [InlineData("WebP")]
    [InlineData("AVIF")]
    public void WithdrawingAPromisedFormatStopsTheRunWithItsName(string format)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Build(format));

        Assert.Contains(format, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And the other half, which is what makes the two ranks worth having: withdrawing a TOLERATED
    /// format is followed by nothing at all. It is reported and the run goes on - nothing is
    /// asserted about it anywhere, so nothing can branch on it (Part 5).
    /// </summary>
    [Fact]
    public void WithdrawingAToleratedFormatIsReportedAndNothingElse()
    {
        var stock = Build("JPEG XL");

        Assert.Contains("JPEG XL", stock.SkippedTolerated, StringComparer.Ordinal);
        Assert.DoesNotContain("JPEG XL", stock.Tolerated.Keys, StringComparer.Ordinal);
        Assert.Equal(TestAssets.MandatoryFormats.Length, stock.Promised.Count);
    }

    /// <summary>
    /// The counter-check to both: withdrawing nothing builds the whole stock. Without it the two
    /// above would pass if the generator simply always threw.
    /// </summary>
    [Fact]
    public void WithdrawingNothingBuildsEverything()
    {
        var stock = Build();

        Assert.Equal(TestAssets.MandatoryFormats.Length, stock.Promised.Count);
        Assert.NotEmpty(stock.Tolerated);
    }

    private static TestAssetSet Build(params string[] withheld)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "dndoverlay-withheld-" + Guid.NewGuid().ToString("N"));

        try
        {
            return TestAssets.Build(directory, new HashSet<string>(withheld, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
