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

    /// <summary>
    /// The third category, and the reason it exists: what the operating system is ASKED lives
    /// here, for both applications, so that two processes derive character-identical ScreenIds
    /// from the same hardware (Part 2).
    /// <para>
    /// It has to be a category rather than an exception. <see cref="Libraries"/> and
    /// <see cref="Applications"/> alone would force the Linux filter rule to demand this project
    /// be built there, where it fails with NETSDK1100 - and an exception inside a rule explains
    /// itself to nobody half a year later.
    /// </para>
    /// </summary>
    internal static readonly string[] Platform =
    [
        "DnDOverlay.Platform.Windows",
    ];

    /// <summary>The two WPF applications.</summary>
    internal static readonly string[] Applications =
    [
        "DnDOverlay.Control",
        "DnDOverlay.Display",
    ];

    /// <summary>
    /// The fourth category: the firewall helpers, and they are a category for the same reason the
    /// platform project is one - not an exception inside a rule.
    /// <para>
    /// Separate executables rather than one with modes, because <b>the UAC prompt names the
    /// program</b>: a prompt reading <c>DnDOverlay.FirewallRemove.exe</c> says what is about to
    /// happen at the moment rights are being asked for, while one showing "Windows Command
    /// Processor" - what calling netsh directly would show - is a prompt nobody reads before
    /// clicking. That is also why there are three rather than two: writing a rule that covers
    /// PUBLIC networks is a different act from writing one for home, and the difference has to be
    /// visible where rights are granted. None of them takes arguments, so there is nothing an
    /// unprivileged caller could steer (Part 9).
    /// </para>
    /// <para>
    /// <b>What is tested of them is what can be:</b> the rule name they derive is a pure function
    /// and is covered in the Windows test project, through the same LINKED source file they share.
    /// Their netsh calls are not - those need elevation, so they are carried by the hand-run.
    /// </para>
    /// </summary>
    internal static readonly string[] Helpers =
    [
        "DnDOverlay.FirewallAdd",
        "DnDOverlay.FirewallAddAnywhere",
        "DnDOverlay.FirewallRemove",
    ];

    /// <summary>Everything under src/ that is Windows-bound - platform, apps and the helpers.</summary>
    internal static readonly string[] WindowsBound = [.. Platform, .. Applications, .. Helpers];

    /// <summary>
    /// Test projects that are Windows-bound, and therefore not in the Linux filter.
    /// <para>
    /// This list is about the TARGET FRAMEWORK and nothing else. A test project may reach into the
    /// project it tests without appearing here - see <see cref="SubjectOf"/> - so the two concerns
    /// stay apart: what a test project may reference is one question, and which platform can build
    /// it is another.
    /// </para>
    /// </summary>
    internal static readonly string[] WindowsBoundTests =
    [
        "DnDOverlay.Platform.Windows.Tests",
    ];

    /// <summary>
    /// The project a test project is about, by the naming convention: <c>X.Tests</c> tests
    /// <c>X</c>.
    /// <para>
    /// <b>A test project reaching into its own subject is not an exception to any rule</b> - it is
    /// what a test project is for. The structure rules describe how ORDINARY projects may depend
    /// on one another; a test project is a special case that is explicitly responsible for exactly
    /// one of them. Without this the rules would have to carry a named exception per test project,
    /// and each one would look like a concession rather than the normal case.
    /// </para>
    /// <para>
    /// <b>The firewall helpers are the one place where it does not hold</b>, and deliberately so:
    /// nobody may reference them, a test project included, because referencing an executable as a
    /// library gives up the name in the elevation prompt. What is testable of them is reached
    /// through a LINKED source file instead (Part 9).
    /// </para>
    /// </summary>
    internal static string SubjectOf(string testProject) =>
        testProject.EndsWith(".Tests", StringComparison.Ordinal)
            ? testProject[..^".Tests".Length]
            : testProject;

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
