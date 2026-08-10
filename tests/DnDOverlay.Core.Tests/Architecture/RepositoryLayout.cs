using System.Xml.Linq;

namespace DnDOverlay.Core.Tests.Architecture;

/// <summary>
/// Reads the project files of the repository. The architecture tests check two different
/// subjects with two different means: the DECLARED structure here (project references,
/// target frameworks, packages - readable while the projects are still empty) and the
/// COMPILED code in <see cref="ArchitectureTests"/> (types, calls, P/Invoke).
/// </summary>
internal static class RepositoryLayout
{
    /// <summary>The five platform-neutral libraries. Apps are deliberately not in here.</summary>
    internal static readonly string[] Libraries =
    [
        "DnDOverlay.Core",
        "DnDOverlay.Hub",
        "DnDOverlay.Campaign",
        "DnDOverlay.Imaging",
        "DnDOverlay.Transport",
    ];

    /// <summary>The two WPF applications - the only projects that may be Windows-bound.</summary>
    internal static readonly string[] Applications =
    [
        "DnDOverlay.Control",
        "DnDOverlay.Display",
    ];

    internal static DirectoryInfo RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Every project below src/, by project name.</summary>
    internal static IReadOnlyDictionary<string, ProjectFile> SourceProjects { get; } =
        LoadProjects("src");

    /// <summary>Every project below tests/, by project name.</summary>
    internal static IReadOnlyDictionary<string, ProjectFile> TestProjects { get; } =
        LoadProjects("tests");

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DnDOverlay.slnx")))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException(
                "Could not find the repository root - no DnDOverlay.slnx above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, ProjectFile> LoadProjects(string folder)
    {
        var root = new DirectoryInfo(Path.Combine(RepositoryRoot.FullName, folder));

        return root
            .EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .Select(ProjectFile.Load)
            .ToDictionary(project => project.Name, StringComparer.Ordinal);
    }
}

/// <summary>One project file, reduced to what the structure rules ask about.</summary>
internal sealed record ProjectFile(
    string Name,
    string Path,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences)
{
    internal static ProjectFile Load(FileInfo file)
    {
        var document = XDocument.Load(file.FullName);

        var frameworks = document
            .Descendants("TargetFramework")
            .Concat(document.Descendants("TargetFrameworks"))
            .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .ToList();

        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => value is not null)
            .Select(value => System.IO.Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))
            .ToList();

        var packageReferences = Includes(document, "PackageReference");
        var frameworkReferences = Includes(document, "FrameworkReference");

        return new ProjectFile(
            System.IO.Path.GetFileNameWithoutExtension(file.Name),
            file.FullName,
            frameworks,
            projectReferences,
            packageReferences,
            frameworkReferences);
    }

    private static List<string> Includes(XDocument document, string elementName) =>
        document
            .Descendants(elementName)
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();
}
