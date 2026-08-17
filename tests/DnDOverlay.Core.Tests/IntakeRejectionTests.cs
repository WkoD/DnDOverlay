using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// The mapping from the picture's vocabulary into the one the DM is answered in.
/// <para>
/// Written as a full table rather than as a handful of examples, and that is the point: the mapping
/// ends in a <c>_ =&gt;</c>, so a value added to <see cref="ImageRejection"/> later would quietly
/// become <see cref="IntakeRejection.Unreadable"/> - which is exactly the silence this whole change
/// was made to end. With the table, adding one fails here and has to be decided rather than
/// inherited.
/// </para>
/// </summary>
public sealed class IntakeRejectionTests
{
    private static readonly Dictionary<ImageRejection, IntakeRejection> Expected = new()
    {
        [ImageRejection.Unreadable] = IntakeRejection.Unreadable,
        [ImageRejection.NotPermitted] = IntakeRejection.NotPermitted,
        [ImageRejection.TooLarge] = IntakeRejection.TooLarge,
        [ImageRejection.Aborted] = IntakeRejection.Aborted,
    };

    /// <summary>
    /// The reasons the DM can be given that no codec produces. They are here so the two vocabularies
    /// are read side by side: <see cref="IntakeRejection"/> is deliberately the wider one, and a
    /// value missing from the table above is only right when it is missing on purpose.
    /// <para>
    /// <see cref="IntakeRejection.NoSpace"/> was in the picture's vocabulary once and had no
    /// producer at all - a codec is asked what a picture IS and can say nothing about a drive. It
    /// read in the mapping as though one could.
    /// </para>
    /// </summary>
    private static readonly HashSet<IntakeRejection> NotFromAPicture =
    [
        IntakeRejection.NoSpace,
        IntakeRejection.Address,
        IntakeRejection.Unreachable,
        IntakeRejection.Unavailable,
    ];

    public static TheoryData<ImageRejection> Reasons => [.. Expected.Keys];

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Every_picture_reason_keeps_its_meaning(ImageRejection reason) =>
        Assert.Equal(Expected[reason], reason.AsIntake());

    /// <summary>
    /// And the table is COMPLETE. Without this the theory would keep passing while a new reason fell
    /// through the default - the failure mode is silence, so the check has to be about the set
    /// rather than about the cases.
    /// </summary>
    [Fact]
    public void No_picture_reason_is_left_out_of_the_table() =>
        Assert.Equal(Enum.GetValues<ImageRejection>().ToHashSet(), Expected.Keys.ToHashSet());

    /// <summary>
    /// And every reason the DM can be given is accounted for: either a picture produces it, or it
    /// is named above as one that cannot. A word added to <see cref="IntakeRejection"/> with no
    /// source at all is the fault this pair of tests exists to prevent - it looks like an answer
    /// and nothing ever gives it.
    /// </summary>
    [Fact]
    public void Every_reason_the_DM_reads_comes_from_somewhere()
    {
        var fromAPicture = Expected.Values.ToHashSet();
        var stranded = Enum.GetValues<IntakeRejection>()
            .Where(reason => !fromAPicture.Contains(reason) && !NotFromAPicture.Contains(reason))
            .ToList();

        Assert.Empty(stranded);

        // Both ways round: a reason cannot be in the picture's table AND named as impossible there.
        Assert.DoesNotContain(NotFromAPicture, fromAPicture.Contains);
    }
}
