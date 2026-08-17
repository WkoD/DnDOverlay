using DnDOverlay.Core;
using DnDOverlay.Transport;

namespace DnDOverlay.Transport.Tests;

/// <summary>
/// The other half of the mapping into the DM's vocabulary: what a refused FETCH becomes.
/// <para>
/// Same shape and same reason as the picture side in <c>Core.Tests</c>. This one carries more,
/// because it is where two of the words stop being obvious: <see cref="FetchRejection.Scheme"/>
/// lands on the ADDRESS - an <c>ftp://</c> address and a loopback address are refused by the same
/// check for the same reason, that we do not fetch from there - and
/// <see cref="FetchRejection.ContentType"/> lands on UNREADABLE, because a page instead of a picture
/// is exactly "what came back is not an image".
/// </para>
/// </summary>
public sealed class FetchRejectionTests
{
    private static readonly Dictionary<FetchRejection, IntakeRejection> Expected = new()
    {
        [FetchRejection.Scheme] = IntakeRejection.Address,
        [FetchRejection.Address] = IntakeRejection.Address,
        [FetchRejection.TooManyRedirects] = IntakeRejection.Unreachable,
        [FetchRejection.Timeout] = IntakeRejection.Unreachable,
        [FetchRejection.TooLarge] = IntakeRejection.TooLarge,
        [FetchRejection.ContentType] = IntakeRejection.Unreadable,
        [FetchRejection.Unreachable] = IntakeRejection.Unreachable,
    };

    public static TheoryData<FetchRejection> Reasons => [.. Expected.Keys];

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Every_fetch_reason_keeps_its_meaning(FetchRejection reason) =>
        Assert.Equal(Expected[reason], reason.AsIntake());

    /// <summary>
    /// The table is complete, so a guard added to the fetch later cannot fall through the default
    /// and arrive at the table as "could not be read".
    /// </summary>
    [Fact]
    public void No_fetch_reason_is_left_out_of_the_table() =>
        Assert.Equal(Enum.GetValues<FetchRejection>().ToHashSet(), Expected.Keys.ToHashSet());
}
