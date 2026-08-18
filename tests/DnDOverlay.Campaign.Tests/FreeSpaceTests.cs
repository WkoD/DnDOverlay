using System.Text;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// "Free space is checked before the ingest" (Part 5).
/// <para>
/// <b>The rule had no check step at all.</b> Part 11 names none, and the nearest thing to it - the
/// stock message under 5 GB - is a different number for a different job in a different milestone:
/// that one WARNS while there is still room, this one refuses when there is none. Found while
/// deriving M2c, and M2c is where it matters: two hundred pictures in one go is the way a drive
/// actually fills up.
/// </para>
/// <para>
/// The space is handed in for the obvious reason: the case worth proving is a full disk, and no
/// test may create one.
/// </para>
/// </summary>
public sealed class FreeSpaceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "dndoverlay-space-" + Guid.NewGuid().ToString("N"));

    private readonly FakeImageCodec _codec = new();
    private readonly FakeTimeProvider _time = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// A full drive refuses with its own reason and names the campaign - the DM's answer is to
    /// clear space, and folded into "too large" that is exactly the answer they would not get.
    /// </summary>
    [Fact]
    public async Task AFullDriveRefusesWithItsOwnReasonAndNamesTheCampaign()
    {
        var store = Open(free: 1024);

        var refused = Assert.IsType<IngestResult.Refused>(
            await store.IngestAsync(Picture, "Dorfkarte", TestContext.Current.CancellationToken));

        Assert.Equal(IntakeRejection.NoSpace, refused.Reason);
        Assert.Contains(_directory, refused.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it refuses BEFORE decoding - the same order as the size limits, and for the same reason:
    /// decoding is the expensive step, and twelve seconds spent on a picture that cannot be written
    /// afterwards help nobody.
    /// </summary>
    [Fact]
    public async Task ItRefusesBeforeTheExpensiveStep()
    {
        var store = Open(free: 1024);

        await store.IngestAsync(Picture, "Dorfkarte", TestContext.Current.CancellationToken);

        Assert.Equal(0, _codec.Normalisations);
    }

    /// <summary>Nothing is left behind: no entry, no file, no half-written thumbnail.</summary>
    [Fact]
    public async Task ARefusedPictureLeavesNothingBehind()
    {
        var store = Open(free: 1024);

        await store.IngestAsync(Picture, "Dorfkarte", TestContext.Current.CancellationToken);

        Assert.Equal(0, store.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "assets"), "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// The counter-check, and it is the half that keeps the guard honest: with room to spare the
    /// picture comes in as always. A limit that fires in normal operation costs more than what it
    /// guards against (Part 4).
    /// </summary>
    [Fact]
    public async Task WithRoomToSpareNothingChanges()
    {
        var store = Open(free: 4L * 1024 * 1024 * 1024);

        Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(Picture, "Dorfkarte", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A drive that cannot be asked about counts as having room. The guard is against a disk
    /// filling up, not against not knowing - and a network share that answers nothing must not turn
    /// every ingest into a refusal.
    /// </summary>
    [Fact]
    public async Task ADriveThatCannotBeAskedCountsAsHavingRoom()
    {
        var store = AssetStore.Open(_directory, _codec, _time, freeSpace: () => long.MaxValue);

        Assert.IsType<IngestResult.Taken>(
            await store.IngestAsync(Picture, "Dorfkarte", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// In a run of many, a full drive stops each picture separately rather than the run - the report
    /// then says what happened, which is what the DM needs in order to go and clear something.
    /// </summary>
    [Fact]
    public async Task InARunEveryPictureIsRefusedSeparatelyRatherThanTheRunFailing()
    {
        var store = Open(free: 1024);

        var report = await new Intake(store, _time).TakeInAsync(
            [Source("Eins"), Source("Zwei"), Source("Drei")],
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, report.Refused.Count);
        Assert.False(report.Cancelled);
    }

    private static byte[] Picture => Encoding.UTF8.GetBytes("a picture of a village");

    private static IntakeSource Source(string name) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(new IntakeBytes.Ready(Picture)));

    private AssetStore Open(long free) =>
        AssetStore.Open(_directory, _codec, _time, freeSpace: () => free);
}
