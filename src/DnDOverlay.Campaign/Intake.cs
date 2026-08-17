using System.Diagnostics;
using DnDOverlay.Core;

namespace DnDOverlay.Campaign;

/// <summary>
/// One thing to take in: a name the entrance derived, and a way to get at the bytes.
/// <para>
/// <b>The bytes come through a delegate rather than as an array</b>, and that is what makes two
/// hundred files the same path as one: they are fetched when their turn comes, so a folder of
/// panoramas does not have to fit in memory before the first of them is taken in. A file reads
/// itself, a paste hands over what it already holds, a URL import fetches - and all three are the
/// same list to the run.
/// </para>
/// </summary>
/// <param name="ProposedName">From the derivation in <see cref="AssetNaming"/> (Part 3).</param>
/// <param name="Open">Gets the bytes, or says why it cannot.</param>
public sealed record IntakeSource(
    string ProposedName, Func<CancellationToken, ValueTask<IntakeBytes>> Open);

/// <summary>What came of reaching for one source's bytes.</summary>
public abstract record IntakeBytes
{
    private IntakeBytes()
    {
    }

    /// <summary>The bytes, as they came in.</summary>
    public sealed record Ready(ReadOnlyMemory<byte> Data) : IntakeBytes;

    /// <summary>
    /// They could not be got at all - a file that vanished between the drop and its turn, an
    /// address that refused. It is a refusal like any other and lands in the same report, because
    /// to the DM there is no difference between "could not be fetched" and "could not be read".
    /// </summary>
    public sealed record Unavailable(IntakeRejection Reason, string Detail) : IntakeBytes;
}

/// <summary>How far a run has got. "37 von 200", over the whole stack rather than per file (Part 7).</summary>
/// <param name="Done">How many are finished, refusals included.</param>
/// <param name="Total">How many there are.</param>
/// <param name="Name">The one being worked on, so the DM can see it move.</param>
public sealed record IntakeProgress(int Done, int Total, string Name);

/// <summary>
/// One that did not come in, with the reason kept for the collected message.
/// <para>
/// <paramref name="Reason"/> is carried rather than re-derived, and that is the whole repair of the
/// M2c finding: it used to hold the detail text only, so the one place that logs a refusal had
/// nothing to say and said <c>Unreadable</c> for every one of them - a pixel bomb, a refused
/// address and a locked file alike.
/// </para>
/// </summary>
public sealed record IntakeFailure(string Name, IntakeRejection Reason, string Detail);

/// <summary>
/// One that came in, with what it cost.
/// </summary>
/// <param name="Standing">
/// Whether the source format was promised or merely tolerated (Part 5). Carried per picture rather
/// than as a second list beside this one, so the two cannot disagree about which picture it was.
/// </param>
/// <param name="Milliseconds">
/// How long THIS picture took - hash, unpack, decode, normalise, thumbnail, write.
/// <para>
/// It is per picture and not only per run, because the run total cannot answer the question it is
/// asked. Two hundred files that took eight seconds say nothing about which one took six of them,
/// and the spread is real: a JPEG hands its bytes through in a millisecond while a 24 MB PNG has
/// its thumbnail unfolded. Without this the line for both reads the same from the outside.
/// </para>
/// <para>
/// <b>It was lost once already.</b> Until M2c the control measured each ingest itself; when the run
/// took the looping over, the measurement did not come with it and <c>2001</c> reported a hard-wired
/// zero for every picture. The M2c hand-run saw "0 ms", read it as "the ingest is fast" and closed
/// the finding - a number that is not measured is worse than no number, because it answers.
/// </para>
/// </param>
public sealed record IntakeTaken(AssetRef Asset, FormatStanding Standing, long Milliseconds);

/// <summary>
/// What a whole run came to - the material for ONE collected message rather than two hundred
/// dialogues (Part 7).
/// </summary>
/// <param name="Taken">Newly taken in, each with its standing and what it cost.</param>
/// <param name="AlreadyPresent">
/// Already in the stock, so nothing was written and the name they carry is the one they already
/// had. Counted separately because "2 doppelt" is an answer and "195 aufgenommen" alone is not.
/// They carry no duration: nothing was decoded, and a zero here would be a measurement of nothing.
/// </param>
/// <param name="Refused">Turned away, each with its reason.</param>
/// <param name="Cancelled">The run was broken off. What was taken in stays - nothing is rolled back.</param>
public sealed record IntakeReport(
    IReadOnlyList<IntakeTaken> Taken,
    IReadOnlyList<AssetRef> AlreadyPresent,
    IReadOnlyList<IntakeFailure> Refused,
    bool Cancelled)
{
    /// <summary>Nothing was asked of it.</summary>
    public static IntakeReport Empty { get; } = new([], [], [], Cancelled: false);

    /// <summary>How many were dealt with, one way or another.</summary>
    public int Count => Taken.Count + AlreadyPresent.Count + Refused.Count;

    /// <summary>
    /// Names of those whose format worked but is not assured (Part 5). Reported rather than hidden:
    /// it went through, and the DM is told that this format is not one of the six promised.
    /// <para>
    /// Read off <see cref="Taken"/> rather than collected beside it. A second list would be a second
    /// place saying which pictures those were, and the two could drift apart with nothing noticing.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Tolerated =>
        [.. Taken.Where(taken => taken.Standing is FormatStanding.Tolerated).Select(taken => taken.Asset.Name)];
}

/// <summary>
/// Taking pictures into the stock - one, or two hundred, along the same path.
/// <para>
/// <b>There is no second, quicker way in.</b> A single paste is this run with one source in it: it
/// is over in a blink and nobody sees it, and that is the point - two paths would be two places for
/// a check to be missing, with an arbitrary threshold deciding which one you got (Part 7).
/// </para>
/// <para>
/// <b>It lives in <c>Campaign</c> rather than in the control</b>, and the reason is the promise it
/// carries: "breaking off leaves what was taken in standing, and rolls nothing back". Written into
/// a window that has to be clicked, that sentence is provable only by hand, twenty times over. Here
/// it is a test.
/// </para>
/// </summary>
public sealed class Intake(IAssetSink stock)
{
    private readonly IAssetSink _stock = stock ?? throw new ArgumentNullException(nameof(stock));

    /// <summary>
    /// Takes every source in, in order, reporting as it goes.
    /// <para>
    /// Nothing here throws for a picture's sake: a refusal, a file that vanished, a container with
    /// nothing in it all end up in the report. The only way out other than the end is the token
    /// being cancelled, and even that returns a report - of what was taken in before it.
    /// </para>
    /// </summary>
    public async Task<IntakeReport> TakeInAsync(
        IReadOnlyList<IntakeSource> sources,
        IProgress<IntakeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return IntakeReport.Empty;
        }

        var taken = new List<IntakeTaken>();
        var known = new List<AssetRef>();
        var refused = new List<IntakeFailure>();

        for (var index = 0; index < sources.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Broken off, and what is already in stays in - every finished picture is a valid
                // entry, so there is nothing a rollback would be putting right (Part 7).
                return new IntakeReport(taken, known, refused, Cancelled: true);
            }

            var source = sources[index];

            progress?.Report(new IntakeProgress(index, sources.Count, source.ProposedName));

            IngestResult outcome;

            // Started here rather than inside the stock, because what is being measured is what the
            // DM waits for: reaching the bytes counts too, and a URL import spends most of its
            // seconds there.
            var clock = Stopwatch.GetTimestamp();

            try
            {
                outcome = await One(source, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Measured, not foreseen: the check at the top of the loop leaves a window - the DM
                // may break off while the progress is being shown, and the ingest then throws on a
                // token that was fine a line earlier. Letting that out would hand the caller an
                // exception instead of the report, and the report is the whole promise: it says
                // what stands. So the break arrives here as what it is, an ending, not a failure.
                return new IntakeReport(taken, known, refused, Cancelled: true);
            }

            switch (outcome)
            {
                case IngestResult.Taken { AlreadyPresent: true } present:
                    known.Add(present.Asset);
                    break;

                case IngestResult.Taken stocked:
                    taken.Add(new IntakeTaken(
                        stocked.Asset,
                        stocked.Standing,
                        (long)Stopwatch.GetElapsedTime(clock).TotalMilliseconds));
                    break;

                case IngestResult.Refused turned:
                    refused.Add(new IntakeFailure(source.ProposedName, turned.Reason, turned.Detail));
                    break;
            }
        }

        progress?.Report(new IntakeProgress(sources.Count, sources.Count, string.Empty));

        return new IntakeReport(taken, known, refused, Cancelled: false);
    }

    private async Task<IngestResult> One(IntakeSource source, CancellationToken cancellationToken)
    {
        try
        {
            return await source.Open(cancellationToken).ConfigureAwait(false) switch
            {
                IntakeBytes.Ready ready => await _stock
                    .IngestAsync(ready.Data, source.ProposedName, cancellationToken)
                    .ConfigureAwait(false),
                IntakeBytes.Unavailable missing =>
                    new IngestResult.Refused(missing.Reason, missing.Detail),
                _ => new IngestResult.Refused(IntakeRejection.Unreadable, "Nothing came of it."),
            };
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A file that is locked or gone is the everyday case in a folder of two hundred, and it
            // must not end the run for the other hundred and ninety-nine.
            return new IngestResult.Refused(IntakeRejection.Unavailable, failure.Message);
        }
    }
}
