using System.Globalization;
using System.Text;
using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// Replacing a file without ever leaving half of one behind (Part 6). The promise the stock leans
/// on hardest is the negative one: a write that fails leaves NOTHING - not a truncated file under
/// a valid hash, and not a scratch file that a later check would believe.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "dndoverlay-atomic-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AWriteReplacesTheWholeFile()
    {
        var path = Path.Combine(_directory, "settings.json");

        AtomicFile.Write(path, "first"u8);
        AtomicFile.Write(path, "second, and rather longer"u8);

        Assert.Equal("second, and rather longer", File.ReadAllText(path));
        Assert.Empty(ScratchFiles());
    }

    /// <summary>
    /// The case the stock's promise rests on: the write fails, and afterwards there is no file
    /// under the target name and no scratch file either. A leftover <c>.tmp</c> under a valid hash
    /// would be a half-written picture that every later check believes (Part 11).
    /// </summary>
    [Fact]
    public void AFailedWriteLeavesNeitherATargetNorAScratchFile()
    {
        // A directory where the file should go: the write succeeds, the rename cannot.
        var path = Path.Combine(_directory, "occupied");
        Directory.CreateDirectory(path);

        Refused(() => AtomicFile.Write(path, "never arrives"u8));

        Assert.Empty(ScratchFiles());
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// And the older content survives it. Half a file would be worse than none, but an EMPTIED one
    /// is the same loss - so the failure must not have touched what was there.
    /// </summary>
    [Fact]
    public void AFailedWriteLeavesTheOlderContentAlone()
    {
        var path = Path.Combine(_directory, "settings.json");
        AtomicFile.Write(path, "the good content"u8);

        // Held open for writing, so the rename onto it cannot go through.
        using (var holder = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            Refused(() => AtomicFile.Write(path, "the new content"u8));
        }

        Assert.Equal("the good content", File.ReadAllText(path));
        Assert.Empty(ScratchFiles());
    }

    /// <summary>
    /// For a CONTENT-ADDRESSED file the answer is the opposite one: whoever got there first wrote
    /// the identical bytes, because the name is the content. Losing that race is success (Part 5).
    /// </summary>
    [Fact]
    public async Task AContentAddressedWriteAcceptsLosingTheRace()
    {
        var path = Path.Combine(_directory, "a".PadRight(64, 'a') + ".png");
        var bytes = Encoding.UTF8.GetBytes("the picture");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(n => Task.Run(
            () => AtomicFile.WriteContentAddressed(
                path, bytes, n.ToString(CultureInfo.InvariantCulture)))));

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(ScratchFiles());
    }

    /// <summary>
    /// The file system refuses in one of two ways, and which one is neither ours to choose nor
    /// stable across platforms - this test runs on Linux too (Part 2). Both are what AtomicFile
    /// itself catches, so both are what the promise is made against.
    /// </summary>
    private static void Refused(Action write)
    {
        var failure = Record.Exception(write);

        Assert.NotNull(failure);
        Assert.True(
            failure is IOException or UnauthorizedAccessException,
            $"the write failed with {failure.GetType().Name}, which is neither of the two expected");
    }

    private string[] ScratchFiles() =>
        Directory.GetFiles(_directory, "*" + AtomicFile.TemporarySuffix, SearchOption.AllDirectories);
}
