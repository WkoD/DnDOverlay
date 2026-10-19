namespace DnDOverlay.Core.Tests;

/// <summary>
/// The search bar's filter (Part 7). The German examples with ß stay in the hand part on purpose
/// (step 40b): a test asserting "strasse" finds "Straße" would be a claim about ICU on the second
/// platform, not about us. What is asserted here holds on both.
/// </summary>
public sealed class NameFilterTests
{
    [Theory]
    [InlineData("Krieg", "Krieger")]
    [InlineData("Krieg", "Willkommen im Krieg")]
    [InlineData("krieg", "Kriegsszene")]
    public void A_substring_anywhere_matches_ignoring_case(string query, string name)
    {
        Assert.True(NameFilter.Matches(NameFilter.Normalise(name), query));
    }

    [Fact]
    public void Diacritics_are_ignored()
    {
        Assert.True(NameFilter.Matches(NameFilter.Normalise("Fürst Vellin"), "furst"));
        Assert.True(NameFilter.Matches(NameFilter.Normalise("Elar'vil Café"), "CAFE"));
    }

    /// <summary>Several words are AND, in any order.</summary>
    [Fact]
    public void Several_words_must_all_be_there_in_any_order()
    {
        var name = NameFilter.Normalise("Szene aus dem Krieg");

        Assert.True(NameFilter.Matches(name, "krieg szene"));
        Assert.False(NameFilter.Matches(name, "krieg taverne"));
    }

    /// <summary>A filter one cannot see must not hide pictures.</summary>
    [Fact]
    public void An_empty_query_passes_everything()
    {
        Assert.True(NameFilter.Matches(NameFilter.Normalise("Ork"), "   "));
    }
}
