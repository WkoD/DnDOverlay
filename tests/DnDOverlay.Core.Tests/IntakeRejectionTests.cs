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
        [ImageRejection.NoSpace] = IntakeRejection.NoSpace,
    };

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
}
