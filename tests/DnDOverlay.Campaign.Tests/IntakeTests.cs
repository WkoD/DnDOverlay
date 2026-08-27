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
    /// Every refusal keeps the reason it was given, and the three sources of a reason stay told
    /// apart: the picture, the address, and the bytes that could not be got at this end.
    /// <para>
    /// This is the test that was missing when it mattered. The report used to carry the detail text
    /// only, the one place that logs a refusal therefore had nothing to say and wrote
    /// <c>Unreadable</c> for all of them - and the M2c hand-run produced sixty-three log lines whose
    /// text was right and whose reason was wrong, with nothing red anywhere (Part 5, Part 8).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_refusal_keeps_the_reason_it_was_given()
    {
        var report = await Run(
            Refusing("Ork von innen", IntakeRejection.Address, "127.0.0.1 is loopback."),
            Refusing("Landkarte", IntakeRejection.Unreachable, "The server answered 403 Forbidden."),
            Broken("Kaputtes Bild"),
            Throwing("Weggeschlossen"));

        Assert.Equal(
            [
                IntakeRejection.Address,
                IntakeRejection.Unreachable,
                IntakeRejection.Unreadable,
                IntakeRejection.Unavailable,
            ],
            report.Refused.Select(failure => failure.Reason));

        // And the text still says which of the two addresses it was - a reason groups, a detail
        // names (Part 7).
        Assert.Contains("127.0.0.1", report.Refused[0].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The collected message's material: taken in, already there, refused - counted apart, because
    /// "195 aufgenommen" alone is not an answer to what happened to the other five (Part 7).
    /// </summary>
    [Fact]
    public async Task TheReportKeepsTheThreeOutcomesApart()
    {
        var stock = Open();
        var intake = new Intake(stock, _time);

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

        await new Intake(stock, _time).TakeInAsync(
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

        var report = await new Intake(stock, _time).TakeInAsync(
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
        foreach (var taken in report.Taken)
        {
            Assert.True(
                stock.TryOpen(taken.Asset.AssetId, out var data, out _), $"{taken.Asset.Name} has no file");
            data.Dispose();
        }
    }

    /// <summary>
    /// Every picture that came in says what IT cost, and the run's own total is not a substitute.
    /// <para>
    /// The number was there until M2c, when the control still looped itself; the run took the
    /// looping over and the measurement did not come with it, so <c>2001</c> reported a hard-wired
    /// zero for every picture. The M2c hand-run saw "0 ms", read it as "the ingest is fast", and
    /// closed the finding - a number that is not measured is worse than none, because it answers.
    /// </para>
    /// <para>
    /// What is asserted is that it is a MEASUREMENT, not a particular duration: a fake codec is
    /// fast and the honest floor is zero. So the source is made to dwell, and the reading has to
    /// follow it - which a constant cannot do.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Every_picture_says_what_it_cost()
    {
        var report = await Run(Dwelling("Langsam", TimeSpan.FromMilliseconds(40)), Source("Schnell"));

        var slow = report.Taken.Single(taken => taken.Asset.Name == "Langsam");
        var quick = report.Taken.Single(taken => taken.Asset.Name == "Schnell");

        Assert.Equal(40, slow.Milliseconds);
        Assert.Equal(0, quick.Milliseconds);
    }

    /// <summary>
    /// The tolerated rank is read off what came in rather than collected beside it - two lists
    /// could disagree about which pictures they meant, with nothing noticing (Part 5).
    /// </summary>
    [Fact]
    public async Task A_tolerated_format_is_named_in_the_report()
    {
        _codec.Standing = FormatStanding.Tolerated;

        var report = await Run(Source("Karte in JPEG XL"));

        Assert.Equal(["Karte in JPEG XL"], report.Tolerated);
        Assert.Equal(FormatStanding.Tolerated, Assert.Single(report.Taken).Standing);
    }

    /// <summary>
    /// A file that vanished between the drop and its turn is the everyday case in a folder of two
    /// hundred, and it must not end the run for the other hundred and ninety-nine.
    /// </summary>
    [Fact]
    public async Task AFileThatCannotBeOpenedIsARefusalLikeAnyOther()
    {
        var stock = Open();

        var report = await new Intake(stock, _time).TakeInAsync(
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
        var report = await new Intake(Open(), _time).TakeInAsync(
            [], progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(0, report.Count);
        Assert.False(report.Cancelled);
    }

    /// <summary>
    /// <b>What goes onto a screen keeps the order it was chosen in</b>, whether a picture was newly
    /// taken in or already in the stock.
    /// <para>
    /// Found at the table (hand-run of M3): the report grouped its outcomes, and the control placed
    /// group by group - all the new ones, then all the known ones. Since the hub hands each arrival
    /// the next depth up, a picture already in the stock landed ON TOP of the ones chosen after it.
    /// Choosing first has to mean lying underneath, so the run's own order is what travels.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WhatGoesOnAScreenKeepsTheOrderItWasChosenIn()
    {
        var intake = new Intake(Open(), _time);

        // "Alt" was put up once before, so the second run finds it in the stock rather than taking
        // it in - which is exactly the case that used to be reordered.
        await intake.TakeInAsync([Source("Alt")], progress: null, TestContext.Current.CancellationToken);

        var report = await intake.TakeInAsync(
            [Source("Alt"), Source("Neu"), Broken("Kaputt"), Source("Noch einer")],
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(["Alt", "Neu", "Noch einer"], [.. report.Placeable.Select(asset => asset.Name)]);

        // And the three tallies say what they always said - they are views onto that one list, not
        // lists of their own, so there is nothing left that could disagree with it.
        Assert.Equal(["Alt"], [.. report.AlreadyPresent.Select(asset => asset.Name)]);
        Assert.Equal(["Neu", "Noch einer"], [.. report.Taken.Select(taken => taken.Asset.Name)]);
        Assert.Equal(["Kaputt"], [.. report.Refused.Select(failure => failure.Name)]);
        Assert.Equal(4, report.Count);
    }

    private async Task<IntakeReport> Run(params IntakeSource[] sources) =>
        await new Intake(Open(), _time).TakeInAsync(sources, progress: null, TestContext.Current.CancellationToken);

    private AssetStore Open() => AssetStore.Open(_directory, _codec, _time);

    private static IntakeSource Source(string name) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(
            new IntakeBytes.Ready(Encoding.UTF8.GetBytes("picture of " + name))));

    /// <summary>
    /// A source that takes its time getting at its bytes - a large file, a slow address. The delay
    /// sits in reaching them rather than in the codec, because that is part of what the DM waits
    /// for and therefore part of what the reading has to cover.
    /// <para>
    /// It DWELLS ON THE RUN'S OWN CLOCK rather than in real time. Waiting for real milliseconds
    /// made the assertion a race: on a loaded build machine the source that dwells for 40 ms and the
    /// one that dwells for none both come out at whatever the scheduler gave them, and the run that
    /// failed in CI reported 587 ms for the quick one against 75 ms for the slow one. Advancing the
    /// handed-in clock measures the same thing - does the reading follow what the picture cost - and
    /// measures it exactly.
    /// </para>
    /// </summary>
    private IntakeSource Dwelling(string name, TimeSpan dwell) =>
        new(name, _ =>
        {
            _time.Advance(dwell);

            return ValueTask.FromResult<IntakeBytes>(
                new IntakeBytes.Ready(Encoding.UTF8.GetBytes("picture of " + name)));
        });

    private static IntakeSource Broken(string name) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(
            new IntakeBytes.Ready(FakeImageCodec.Unreadable)));

    private static IntakeSource Missing(string name) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(
            new IntakeBytes.Unavailable(IntakeRejection.Unreachable, "The address answered 404.")));

    /// <summary>A source that could not be got at all, with the reason its entrance gave.</summary>
    private static IntakeSource Refusing(string name, IntakeRejection reason, string detail) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(new IntakeBytes.Unavailable(reason, detail)));

    private static IntakeSource Throwing(string name) =>
        new(name, _ => throw new FileNotFoundException("The file is gone."));

    private sealed class Reporter(Action<IntakeProgress> onStep) : IProgress<IntakeProgress>
    {
        public void Report(IntakeProgress value) => onStep(value);
    }
}
