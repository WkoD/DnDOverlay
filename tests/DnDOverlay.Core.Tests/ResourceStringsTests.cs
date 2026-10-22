using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DnDOverlay.Core.Tests.Architecture;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// The applications' visible texts (Part 8): every member of <c>Strings</c> has an English and a
/// German entry, every entry has a member, and both languages fill the same numbered places. Read
/// from the sources - the two applications have no test project, and these are facts about files.
/// </summary>
public sealed partial class ResourceStringsTests
{
    public static TheoryData<string> Applications => ["DnDOverlay.Display"];

    [Theory]
    [MemberData(nameof(Applications))]
    public void Every_text_is_there_in_both_languages_and_used(string application)
    {
        var directory = Path.Combine(RepositoryLayout.RepositoryRoot.FullName, "src", application);
        // Strings.cs and its partial halves, one per area of the application.
        var members = Directory.EnumerateFiles(directory, "Strings*.cs")
            .SelectMany(file => Member().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var english = Entries(Path.Combine(directory, "Strings.resx"));
        var german = Entries(Path.Combine(directory, "Strings.de.resx"));

        Assert.Empty(members.Except(english.Keys));
        Assert.Empty(english.Keys.Except(members));
        Assert.Empty(english.Keys.Except(german.Keys));
        Assert.Empty(german.Keys.Except(english.Keys));
        Assert.Empty(english.Where(entry => string.IsNullOrWhiteSpace(entry.Value) || string.IsNullOrWhiteSpace(german[entry.Key])).Select(entry => entry.Key));
    }

    /// <summary>A place missing in one language would drop a value silently; one too many throws in front of the DM.</summary>
    [Theory]
    [MemberData(nameof(Applications))]
    public void Both_languages_fill_the_same_places(string application)
    {
        var directory = Path.Combine(RepositoryLayout.RepositoryRoot.FullName, "src", application);
        var english = Entries(Path.Combine(directory, "Strings.resx"));
        var german = Entries(Path.Combine(directory, "Strings.de.resx"));

        var wrong = english
            .Where(entry => german.TryGetValue(entry.Key, out var translated) && !Places(entry.Value).SetEquals(Places(translated)))
            .Select(entry => $"{entry.Key}: \"{entry.Value}\" against \"{german[entry.Key]}\"")
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void A_setting_becomes_the_culture_the_resources_are_looked_up_in()
    {
        var system = CultureInfo.GetCultureInfo("de-AT");

        Assert.Equal("en", UiLanguages.CultureOf(UiLanguage.English, system).Name);
        Assert.Equal("de", UiLanguages.CultureOf(UiLanguage.German, CultureInfo.GetCultureInfo("en-US")).Name);
        Assert.Same(system, UiLanguages.CultureOf(UiLanguage.Automatic, system));
    }

    private static Dictionary<string, string> Entries(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element => (string)element.Attribute("name")!,
            element => element.Element("value")!.Value,
            StringComparer.Ordinal);

    private static HashSet<string> Places(string text) =>
        [.. Place().Matches(text).Select(match => match.Groups[1].Value)];

    [GeneratedRegex(@"Text(?:\[|\.Format\()nameof\((\w+)\)")]
    private static partial Regex Member();

    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex Place();
}
