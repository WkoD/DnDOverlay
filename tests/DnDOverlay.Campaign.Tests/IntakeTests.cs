using System.Text;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// One path for one picture as for two hundred (Part 7, Part 10).
/// <para>
/// <b>These are tests because the run lives in the library.</b> Written into a window, its central
/// promise - "breaking off leaves what was taken in standing and rolls nothing back" - would be
/// provable only by hand, and only by somebody willing to stage it twenty times.
/// </para>
/// </summary>
public sealed class IntakeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "dndoverlay-intake-" + Guid.NewGuid().ToString("N"));

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
    /// A single paste is this run with one source in it. It is over in a blink and nobody sees it -
    /// which is the argument against building a second, quicker way in.
    /// </summary>
    [Fact]
    public async Task OneSourceIsTheSameRunAsTwoHundred()
    {
        var report = await Run(Source("Dorfkarte"));

        Assert.Single(report.Taken);
        Assert.Empty(report.Refused);
        Assert.False(report.Cancelled);
    }

    /// <summary>
    /// The collected message's material: taken in, already there, refused - counted apart, because
    /// "195 aufgenommen" alone is not an answer to what happened to the other five (Part 7).
    /// </summary>
    [Fact]
    public async Task TheReportKeepsTheThreeOutcomesApart()
    {
        var stock = Open();
        var intake = new Intake(stock);

        // The same bytes twice, so the second is a duplicate rather than a second entry.
        var twice = Source("Zwilling");

        var report = await intake.TakeInAsync(
            [Source("Ork"), twice, twice, Broken("Kaputt"), Missing("Weg")],
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, report.Taken.Count);
        Assert.Single(report.AlreadyPresent);
        Assert.Equal(2, report.Refused.Count);
        Assert.Equal(5, report.Count);
    }

    /// <summary>
    /// "37 von 200", over the whole stack rather than per file - and the last report says the run
    /// is done, so a progress bar has something to end on.
    /// </summary>
    [Fact]
    public async Task ProgressCountsTheStackAndEndsOnTheTotal()
    {
        var seen = new List<IntakeProgress>();
        var stock = Open();

        var sources = Enumerable.Range(0, 5)
            .Select(number => Source("Bild " + number))
            .ToArray();

        await new Intake(stock).TakeInAsync(
            sources,
            new Reporter(seen.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(6, seen.Count);
        Assert.All(seen, step => Assert.Equal(5, step.Total));
        Assert.Equal(0, seen[0].Done);
        Assert.Equal("Bild 0", seen[0].Name);
        Assert.Equal(5, seen[^1].Done);
    }

    /// <summary>
    /// <b>The promise that had to be a test.</b> Broken off in the middle: what was taken in stays,
    /// nothing is rolled back, and the report says it was broken off rather than pretending to be
    /// complete. Every finished picture is a valid entry - there is nothing a rollback would put
    /// right (Part 7).
    /// </summary>
    [Fact]
    public async Task BreakingOffLeavesWhatWasTakenInStanding()
    {
        var stock = Open();
        using var stopping = new CancellationTokenSource();

        var sources = Enumerable.Range(0, 10)
            .Select(number => Source("Bild " + number))
            .ToArray();

        var report = await new Intake(stock).TakeInAsync(
            sources,
            new Reporter(step =>
            {
                if (step.Done == 3)
                {
                    stopping.Cancel();
                }
            }),
            stopping.Token);

        Assert.True(report.Cancelled);
        Assert.Equal(3, report.Taken.Count);
        Assert.Equal(3, stock.Count);

        // And what stands is really there, not half written.
        foreach (var asset in report.Taken)
        {
            Assert.True(stock.TryOpen(asset.AssetId, out var data, out _), $"{asset.Name} has no file");
            data.Dispose();
        }
    }

    /// <summary>
    /// A file that vanished between the drop and its turn is the everyday case in a folder of two
    /// hundred, and it must not end the run for the other hundred and ninety-nine.
    /// </summary>
    [Fact]
    public async Task AFileThatCannotBeOpenedIsARefusalLikeAnyOther()
    {
        var stock = Open();

        var report = await new Intake(stock).TakeInAsync(
            [Throwing("Verschwunden"), Source("Danach")],
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Single(report.Taken);
        Assert.Single(report.Refused);
        Assert.Equal("Verschwunden", report.Refused[0].Name);
    }

    /// <summary>Nothing asked, nothing reported - and no message about zero pictures.</summary>
    [Fact]
    public async Task AnEmptyRunIsAnEmptyReport()
    {
        var report = await new Intake(Open()).TakeInAsync(
            [], progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(0, report.Count);
        Assert.False(report.Cancelled);
    }

    private async Task<IntakeReport> Run(params IntakeSource[] sources) =>
        await new Intake(Open()).TakeInAsync(sources, progress: null, TestContext.Current.CancellationToken);

    private AssetStore Open() => AssetStore.Open(_directory, _codec, _time);

    private static IntakeSource Source(string name) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(
            new IntakeBytes.Ready(Encoding.UTF8.GetBytes("picture of " + name))));

    private static IntakeSource Broken(string name) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(
            new IntakeBytes.Ready(FakeImageCodec.Unreadable)));

    private static IntakeSource Missing(string name) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(
            new IntakeBytes.Unavailable("The address answered 404.")));

    private static IntakeSource Throwing(string name) =>
        new(name, _ => throw new FileNotFoundException("The file is gone."));

    private sealed class Reporter(Action<IntakeProgress> onStep) : IProgress<IntakeProgress>
    {
        public void Report(IntakeProgress value) => onStep(value);
    }
}
