namespace DnDOverlay.Core.Tests;

/// <summary>
/// The promise behind <c>--data</c>: it moves EVERYTHING at once (Part 9). These tests are the
/// automated half of acceptance step 7b - that a development run touches no file of the
/// installed copy. They can only check that all five places derive from one value; that the
/// value itself is handed in is the architecture test's job.
/// </summary>
public sealed class DataRootTests
{
    private static readonly DataRoot Root = new(Path.Combine("C:", "somewhere", "DnDOverlay"));

    [Fact]
    public void Every_place_lies_below_the_root()
    {
        foreach (var place in Places(Root))
        {
            Assert.StartsWith(Root.Path, place, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Two roots share no path. Without this, "--data moved everything" could be true for four
    /// places and false for the fifth, and the run would quietly write into the installed copy.
    /// </summary>
    [Fact]
    public void Two_roots_have_nothing_in_common()
    {
        var other = new DataRoot(Path.Combine("C:", "elsewhere", "dev-data"));

        Assert.Empty(Places(Root).Intersect(Places(other), StringComparer.Ordinal));
    }

    [Fact]
    public void The_five_places_are_distinct()
    {
        var places = Places(Root);

        Assert.Equal(places.Length, places.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The campaign folder is the shipped default, not the path in force - the DM may relocate
    /// it, and the effective value lives in control.json (Part 3). The name has to say so,
    /// otherwise somebody reads the default as the answer.
    /// </summary>
    [Fact]
    public void The_campaign_folder_is_named_as_a_default()
    {
        Assert.EndsWith("campaigns", Root.CampaignsDefault, StringComparison.Ordinal);
    }

    private static string[] Places(DataRoot root) =>
    [
        root.ControlConfiguration,
        root.DisplayConfiguration,
        root.CampaignsDefault,
        root.Cache,
        root.Logs,
    ];
}
