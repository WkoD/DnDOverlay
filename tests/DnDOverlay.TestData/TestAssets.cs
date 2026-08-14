using System.Collections.Immutable;
using DnDOverlay.Imaging;

namespace DnDOverlay.TestData;

/// <summary>
/// Builds the whole stock into a directory handed in, once per test run (Part 10). Test data is
/// GENERATED rather than obtained: in a public repository a checked-in test image is a
/// publication, and then somebody has to answer for its rights. Nothing here is committed.
/// <para>
/// The generator is at the same time the CAPABILITY PROBE for this Magick build. What it can write
/// the build can also read - and what it cannot write drops out of the promise (Part 5). The
/// question "what does this build bring along?" therefore answers itself on the first test run
/// instead of being looked up one format at a time, and it answers itself per platform.
/// </para>
/// </summary>
public static class TestAssets
{
    /// <summary>
    /// The promise, as a fixed list (Part 5). Losing one of these is not a shrunken stock but a
    /// broken promise, so the run fails with the format's name - the mechanism that catches the
    /// routine Magick.NET bump which would otherwise take WebP out of the README while everything
    /// stays green.
    /// </summary>
    public static readonly ImmutableArray<string> MandatoryFormats =
        ["PNG", "JPEG", "GIF", "BMP", "WebP", "AVIF"];

    /// <summary>
    /// Builds everything into <paramref name="directory"/> and reports what came of it. The
    /// directory is handed in rather than derived (rule 10); each test project gets its own, and
    /// both are ignored and rebuilt on every run.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A mandatory format could not be written, or the coder policy is not in force.
    /// </exception>
    public static TestAssetSet Build(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        // The generator writes through Magick, so the policy has to be in force here as well -
        // and this is where its width is proved: shut a mandatory coder out and the generator
        // fails first, with that format's name (Part 5).
        CoderPolicy.EnsureApplied();

        Directory.CreateDirectory(directory);

        var images = ImageFiles.Write(directory);

        // The crafted half borrows one genuine PNG: the file with the lying extension has to be a
        // real image, and the truncated one has to be a real image cut in half.
        var crafted = CraftedFiles.Write(directory, images.Promised["PNG"]);
        var tokens = TokenFiles.Write(directory, images.Portrait, images.MapToken);

        return new TestAssetSet(
            directory, images.Promised, images.Tolerated, images.Skipped, crafted, tokens);
    }
}
