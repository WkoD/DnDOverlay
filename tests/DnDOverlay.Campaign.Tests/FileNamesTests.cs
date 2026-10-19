namespace DnDOverlay.Campaign.Tests;

/// <summary>
/// The names that become file names (Part 3): refused with a reason when typed, cleaned only when
/// suggested - and the same rule on every platform, which is the point of keeping the list here
/// rather than asking the framework (it answers differently on Linux, where this also runs).
/// </summary>
public sealed class FileNamesTests
{
    [Theory]
    [InlineData("Rat 3/4")]
    [InlineData("Rat 3\\4")]
    [InlineData("Wer?")]
    [InlineData("a*b")]
    [InlineData("\"x\"")]
    [InlineData("a<b>")]
    [InlineData("a|b")]
    [InlineData("C:x")]
    [InlineData("tab\there")]
    public void Characters_Windows_forbids_are_refused_everywhere(string name)
    {
        Assert.Equal(NameProblem.ForbiddenCharacter, FileNames.Check(name));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL.json")]
    [InlineData("com7")]
    [InlineData("LPT1.txt")]
    public void Reserved_device_names_are_refused_with_or_without_an_extension(string name)
    {
        Assert.Equal(NameProblem.ReservedName, FileNames.Check(name));
    }

    [Theory]
    [InlineData("Dorfkarte.")]
    [InlineData("Dorfkarte ")]
    [InlineData(".")]
    [InlineData("..")]
    public void A_dot_or_space_at_the_end_is_refused(string name)
    {
        Assert.Equal(NameProblem.BadEnding, FileNames.Check(name));
    }

    [Fact]
    public void Empty_and_overlong_names_are_refused()
    {
        Assert.Equal(NameProblem.Empty, FileNames.Check("   "));
        Assert.Equal(NameProblem.TooLong, FileNames.Check(new string('x', FileNames.MaxLength + 1)));
        Assert.Null(FileNames.Check(new string('x', FileNames.MaxLength)));
    }

    [Theory]
    [InlineData("Dorfkarte")]
    [InlineData("Rat der Stadt (alt)")]
    [InlineData("Sturmküste – Kapitel 2")]
    [InlineData("CONSOLE")]
    public void Ordinary_names_pass_unchanged(string name)
    {
        Assert.Null(FileNames.Check(name));
    }

    /// <summary>One dash per forbidden character, nothing drawn together - so the border between device and screen stays visible.</summary>
    [Fact]
    public void A_suggestion_replaces_each_forbidden_character_and_draws_nothing_together()
    {
        Assert.Equal("Szene TISCH-PC--DISPLAY1 2026-08-06 23-41",
            FileNames.Suggest("Szene TISCH-PC//DISPLAY1 " + FileNames.Stamp(new DateTimeOffset(2026, 8, 6, 23, 41, 0, TimeSpan.FromHours(2)))));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("a/b.")]
    [InlineData("")]
    [InlineData("..")]
    public void Every_suggestion_passes_the_check(string raw)
    {
        Assert.Null(FileNames.Check(FileNames.Suggest(raw)));
    }

    /// <summary>Where the application names things itself it cannot ask, so it numbers - ignoring case, as everywhere.</summary>
    [Fact]
    public void Numbering_finds_the_first_free_name_ignoring_case()
    {
        Assert.Equal("Ork", FileNames.Numbered("Ork", ["Goblin"]));
        Assert.Equal("Ork (3)", FileNames.Numbered("Ork", ["ork", "ORK (2)"]));
    }

    /// <summary>Ordinal, not culture-aware: on a Turkish system a cultural comparison keeps I and i apart, and the overwrite question would not come.</summary>
    [Fact]
    public void Names_compare_the_same_under_a_Turkish_culture()
    {
        var before = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            Assert.True(FileNames.Comparer.Equals("DORFKARTE", "dorfkarte"));
            Assert.True(FileNames.Comparer.Equals("INSEL", "insel"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}
