using System.Text.RegularExpressions;
using System.Xml.Linq;
using DnDOverlay.Core.Tests.Architecture;

namespace DnDOverlay.Core.Tests.Logging;

/// <summary>
/// Reads every event identifier out of the three places one can stand: the
/// <c>[LoggerMessage]</c> declarations in the source, the neutral resource catalogue, and the
/// table in <c>docs/protocol.md</c>.
/// <para>
/// Read from SOURCE rather than from assemblies, and that is not laziness: <c>ControlLog</c> and
/// <c>DisplayLog</c> live in the two WPF applications, which this test project cannot reference
/// and which are not even built on Linux. Reading the files covers all seven projects on both
/// platforms - and the attribute is exactly as declarative as an assembly would be.
/// </para>
/// </summary>
internal static class EventCatalogue
{
    /// <summary>
    /// Identifiers that exist without a <c>[LoggerMessage]</c>, with the reason they have to.
    /// <para>
    /// 4008 is raised by <c>ProcessLog</c> when the log file itself gives up, and it deliberately
    /// does NOT go through <c>ILogger</c> - that would return into the sink that is failing. It is
    /// the one message that has to be built by hand, so it is the one exception here.
    /// </para>
    /// </summary>
    internal static readonly int[] HandBuilt = [4008];

    private static readonly Regex Declaration = new(
        """
        \[LoggerMessage\(\s*
            EventId\s*=\s*(?<id>\d+)\s*,\s*
            Level\s*=\s*LogLevel\.(?<level>\w+)\s*,\s*
            Message\s*=\s*(?<message>(?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)\)\]\s*
        (?:internal|public|private)\s+static\s+partial\s+void\s+(?<name>\w+)\s*\(
        """,
        RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant);

    private static readonly Regex Literal = new(
        """ "((?:[^"\\]|\\.)*)" """.Trim(),
        RegexOptions.CultureInvariant);

    private static readonly Regex Placeholder = new(
        @"\{(\w+)\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex CatalogueRow = new(
        @"^\|\s*(?<id>\d{4})\s*\|\s*`(?<name>\w+)`\s*\|\s*(?<level>\w+)\s*\|",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex NextFree = new(
        @"\*\*Next free:\s*(?<id>\d{4})",
        RegexOptions.CultureInvariant);

    /// <summary>What the source declares, across all of <c>src/</c>.</summary>
    internal static IReadOnlyList<DeclaredEvent> Declared { get; } = ReadDeclarations();

    /// <summary>What the neutral catalogue holds, by resource key.</summary>
    internal static IReadOnlyDictionary<string, string> Catalogue { get; } = ReadCatalogue();

    /// <summary>What the table in <c>docs/protocol.md</c> lists.</summary>
    internal static IReadOnlyList<DocumentedEvent> Documented { get; } = ReadDocumentation();

    /// <summary>The "Next free" claims of the documentation, in the order they appear.</summary>
    internal static IReadOnlyList<int> NextFreeClaims { get; } = ReadNextFree();

    internal static IReadOnlyList<string> PlaceholdersOf(string template) =>
        [.. Placeholder.Matches(template).Select(match => match.Groups[1].Value)];

    private static List<DeclaredEvent> ReadDeclarations()
    {
        var source = new DirectoryInfo(Path.Combine(RepositoryLayout.RepositoryRoot.FullName, "src"));
        var declared = new List<DeclaredEvent>();

        foreach (var file in source.EnumerateFiles("*Log.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in Declaration.Matches(File.ReadAllText(file.FullName)))
            {
                declared.Add(new DeclaredEvent(
                    int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    match.Groups["name"].Value,
                    match.Groups["level"].Value,
                    Joined(match.Groups["message"].Value),
                    file.Name));
            }
        }

        return declared;
    }

    /// <summary>Puts a message template that was split across concatenated literals back together.</summary>
    private static string Joined(string message) =>
        string.Concat(Literal
            .Matches(message)
            .Select(match => match.Groups[1].Value.Replace("\\\"", "\"", StringComparison.Ordinal)));

    private static Dictionary<string, string> ReadCatalogue()
    {
        var path = Path.Combine(
            RepositoryLayout.RepositoryRoot.FullName,
            "src",
            "DnDOverlay.Core",
            "Logging",
            "LogMessages.resx");

        return XDocument
            .Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")!.Value,
                StringComparer.Ordinal);
    }

    private static List<DocumentedEvent> ReadDocumentation() =>
        [.. CatalogueRow
            .Matches(Protocol())
            .Select(match => new DocumentedEvent(
                int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["name"].Value,
                match.Groups["level"].Value))];

    private static List<int> ReadNextFree() =>
        [.. NextFree
            .Matches(Protocol())
            .Select(match => int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture))];

    private static string Protocol() =>
        File.ReadAllText(Path.Combine(RepositoryLayout.RepositoryRoot.FullName, "docs", "protocol.md"));
}

/// <summary>One <c>[LoggerMessage]</c> as it stands in the source.</summary>
internal sealed record DeclaredEvent(int Id, string Name, string Level, string Message, string File)
{
    internal string Key => $"E{Id}_{Name}";
}

/// <summary>One row of the catalogue table in <c>docs/protocol.md</c>.</summary>
internal sealed record DocumentedEvent(int Id, string Name, string Level);
