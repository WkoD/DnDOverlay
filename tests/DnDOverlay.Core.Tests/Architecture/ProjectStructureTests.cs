using System.Xml.Linq;

namespace DnDOverlay.Core.Tests.Architecture;

/// <summary>
/// The declared structure of the repository. These rules bite while the projects are still
/// empty, which is the whole point: a reference from Hub into Campaign would compile without
/// complaint and would be impossible to unpick half a year later (Part 2).
/// </summary>
public sealed class ProjectStructureTests
{
    /// <summary>
    /// Core knows no other project of ours, and of the outside world it knows exactly one thing:
    /// the framework's logging abstraction.
    /// <para>
    /// It had none at all until M1b, and the exception is named rather than relaxed. The log
    /// provider lives in Core because BOTH applications need it and Core is the one thing they
    /// share; <c>ILogger</c> is the seam the whole of Part 8 is built on, and the sink behind it is
    /// hand-written precisely so that nothing further comes in. A second package here is a
    /// decision, and this test is what makes it one.
    /// </para>
    /// </summary>
    [Fact]
    public void Core_references_nothing_but_the_logging_abstraction()
    {
        var core = RepositoryLayout.SourceProjects["DnDOverlay.Core"];

        Assert.Empty(core.ProjectReferences);
        Assert.Equal(["Microsoft.Extensions.Logging.Abstractions"], core.PackageReferences);
    }

    [Theory]
    [InlineData("DnDOverlay.Hub")]
    [InlineData("DnDOverlay.Campaign")]
    [InlineData("DnDOverlay.Imaging")]
    [InlineData("DnDOverlay.Transport")]
    public void Libraries_reference_only_Core(string library)
    {
        var project = RepositoryLayout.SourceProjects[library];

        Assert.All(
            project.ProjectReferences,
            reference => Assert.Equal("DnDOverlay.Core", reference));
    }

    /// <summary>
    /// The arrangement belongs to the hub, the material to the campaign (Part 1, idea 3).
    /// This is not a technical impossibility like the target framework but a decision, so it
    /// is the rule that needs a net most.
    /// </summary>
    [Fact]
    public void Hub_does_not_know_Campaign()
    {
        var hub = RepositoryLayout.SourceProjects["DnDOverlay.Hub"];

        Assert.DoesNotContain("DnDOverlay.Campaign", hub.ProjectReferences, StringComparer.Ordinal);
    }

    [Fact]
    public void Campaign_does_not_know_the_Hub()
    {
        var campaign = RepositoryLayout.SourceProjects["DnDOverlay.Campaign"];

        Assert.DoesNotContain("DnDOverlay.Hub", campaign.ProjectReferences, StringComparer.Ordinal);
    }

    /// <summary>
    /// Magick.NET lives in Imaging alone. Transport is checked explicitly, because two costed
    /// promises hang on it - the WinUI fallback and the slim Display MSI (Part 2).
    /// </summary>
    [Fact]
    public void Only_Imaging_references_Magick()
    {
        var offenders = RepositoryLayout.SourceProjects.Values
            .Where(project => project.Name != "DnDOverlay.Imaging")
            .Where(project => project.PackageReferences.Any(
                package => package.StartsWith("Magick.NET", StringComparison.Ordinal)))
            .Select(project => project.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The rule applies to src/, not to tests/: the test data generator references Magick.NET
    /// on purpose, because it WRITES formats (Part 2, Part 10).
    /// </summary>
    [Fact]
    public void The_test_data_generator_references_the_same_Magick_variant_as_Imaging()
    {
        var imaging = RepositoryLayout.SourceProjects["DnDOverlay.Imaging"];
        var generator = RepositoryLayout.TestProjects["DnDOverlay.TestData"];

        var imagingPackage = Assert.Single(
            imaging.PackageReferences,
            package => package.StartsWith("Magick.NET", StringComparison.Ordinal));
        var generatorPackage = Assert.Single(
            generator.PackageReferences,
            package => package.StartsWith("Magick.NET", StringComparison.Ordinal));

        Assert.Equal(imagingPackage, generatorPackage);
    }

    /// <summary>
    /// The five libraries build on net10.0, so a WPF reference is a compile error rather than
    /// a test finding. Only the two applications are Windows-bound (Part 2).
    /// </summary>
    [Fact]
    public void The_five_libraries_are_platform_neutral()
    {
        foreach (var name in RepositoryLayout.Libraries)
        {
            var framework = Assert.Single(RepositoryLayout.SourceProjects[name].TargetFrameworks);
            Assert.Equal("net10.0", framework);
        }
    }

    [Fact]
    public void Everything_Windows_bound_says_so_in_its_target_framework()
    {
        foreach (var name in RepositoryLayout.WindowsBound)
        {
            var framework = Assert.Single(RepositoryLayout.SourceProjects[name].TargetFrameworks);
            Assert.Equal("net10.0-windows", framework);
        }
    }

    /// <summary>
    /// The platform project answers questions; it has no window of its own. Without this,
    /// "what changes a window stays with its application" would be a habit rather than a
    /// property of the build - and the first WPF type dragged in here would take the boundary
    /// with it (Part 2).
    /// </summary>
    [Fact]
    public void The_platform_project_carries_no_user_interface()
    {
        foreach (var name in RepositoryLayout.Platform)
        {
            var project = XDocument.Load(RepositoryLayout.SourceProjects[name].Path);

            var useWpf = project.Descendants("UseWPF").Select(element => element.Value).ToList();
            var useForms = project.Descendants("UseWindowsForms").Select(element => element.Value);

            Assert.Equal(["false"], useWpf);
            Assert.Empty(useForms);
        }
    }

    /// <summary>
    /// The one rule that makes the Windows-bound categories worth having: they are for the
    /// applications, and for nobody else. A platform-neutral library that reached into one of them
    /// would be Windows-bound through the back door, and the Linux job would be where that was
    /// found out.
    /// <para>
    /// <b>Their own tests are not an exception to this</b> - they are what a test project is for.
    /// The rule describes how ordinary projects may depend on one another; a test project is
    /// explicitly responsible for its own subject, and <see cref="RepositoryLayout.SubjectOf"/> is
    /// what says which that is.
    /// </para>
    /// <para>
    /// <b>Exempt is the OWN subject, not the category.</b> While there was one Windows-bound
    /// library the two were the same sentence, and generalising the rule to two of them quietly
    /// pulled them apart: read as a category it would let the platform tests reach into the
    /// rendering project and back. A rule that grows a hole while being widened is the quietest
    /// kind there is.
    /// </para>
    /// </summary>
    [Fact]
    public void Nobody_but_the_applications_and_its_own_tests_reaches_into_a_Windows_bound_library()
    {
        var offenders = RepositoryLayout.Libraries
            .Select(name => RepositoryLayout.SourceProjects[name])
            .Concat(RepositoryLayout.TestProjects.Values)
            .Where(project => project.ProjectReferences
                .Intersect(RepositoryLayout.WindowsBoundLibraries, StringComparer.Ordinal)
                .Any(reference => !string.Equals(
                    reference, RepositoryLayout.SubjectOf(project.Name), StringComparison.Ordinal)))
            .Select(project => project.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The counterpart to <see cref="The_platform_project_carries_no_user_interface"/>, and it is
    /// the reason the rendering project is a category rather than a second platform project: it
    /// has to declare WPF.
    /// <para>
    /// Without this, dropping <c>UseWPF</c> would leave a project that looks like the platform
    /// project, sits in a category that exists solely to keep WPF out of that one, and explains
    /// itself to nobody. The seam it carries would simply stop compiling, which is loud - but the
    /// category would have gone quiet, and that is what this catches.
    /// </para>
    /// </summary>
    [Fact]
    public void The_rendering_project_declares_the_user_interface_framework_it_exists_for()
    {
        foreach (var name in RepositoryLayout.Rendering)
        {
            var project = XDocument.Load(RepositoryLayout.SourceProjects[name].Path);

            var useWpf = project.Descendants("UseWPF").Select(element => element.Value).ToList();

            Assert.Equal(["true"], useWpf);
        }
    }

    /// <summary>
    /// A test project declared Windows-bound has to actually be one. Without this, adding a name to
    /// <see cref="RepositoryLayout.WindowsBoundTests"/> would be a way to take a platform-neutral
    /// project out of the Linux job silently - the quietest way for a net to fail (Part 2).
    /// </summary>
    [Fact]
    public void A_test_project_declared_Windows_bound_targets_Windows()
    {
        foreach (var name in RepositoryLayout.WindowsBoundTests)
        {
            var framework = Assert.Single(RepositoryLayout.TestProjects[name].TargetFrameworks);

            Assert.Equal("net10.0-windows", framework);
        }
    }

    /// <summary>
    /// The counterpart to <see cref="RepositoryLayout.SubjectOf"/>: a test project has a subject
    /// that exists. Otherwise a typo in a project name would quietly widen every rule that grants
    /// a test project access to its own subject - it would simply match nothing.
    /// </summary>
    [Fact]
    public void Every_test_project_is_named_after_a_project_that_exists()
    {
        var orphans = RepositoryLayout.TestProjects.Keys
            .Where(name => name.EndsWith(".Tests", StringComparison.Ordinal))
            .Where(name => !RepositoryLayout.SourceProjects.ContainsKey(RepositoryLayout.SubjectOf(name)))
            .ToList();

        Assert.Empty(orphans);
    }

    /// <summary>
    /// A Windows-bound library may know Core and nothing else. The platform project produces Core
    /// types; the rendering project takes bytes and gives back a bitmap and today knows nobody at
    /// all. What neither may reach is Imaging - the display's slim MSI is a costed promise, and
    /// Magick.NET would be thirty megabytes of it (Part 2, Part 9).
    /// </summary>
    [Fact]
    public void A_Windows_bound_library_references_only_Core()
    {
        foreach (var name in RepositoryLayout.WindowsBoundLibraries)
        {
            Assert.All(
                RepositoryLayout.SourceProjects[name].ProjectReferences,
                reference => Assert.Equal("DnDOverlay.Core", reference));
        }
    }

    /// <summary>
    /// A helper is one self-contained executable. A project reference would put an assembly beside
    /// it that has to be deployed with it and says nothing to the person reading the UAC prompt -
    /// what the two share is a LINKED SOURCE FILE, and this is what keeps it that way (Part 9).
    /// </summary>
    [Fact]
    public void The_firewall_helpers_reference_nothing_at_all()
    {
        foreach (var name in RepositoryLayout.Helpers)
        {
            var project = RepositoryLayout.SourceProjects[name];

            Assert.Empty(project.ProjectReferences);
            Assert.Empty(project.PackageReferences);
        }
    }

    /// <summary>
    /// Nobody may take a helper as a library. It is an executable whose whole point is the name in
    /// the elevation prompt; calling into it as code would give that up without anyone noticing.
    /// <para>
    /// <b>This is the one rule a test project gets no exception from</b> - unlike the platform
    /// project, where reaching into one's own subject is the normal case. A helper has nothing a
    /// reference could reach that a linked source file cannot, and the reference would cost the
    /// property the helpers exist for (Part 9).
    /// </para>
    /// </summary>
    [Fact]
    public void Neither_a_library_nor_a_test_reaches_into_a_firewall_helper()
    {
        var offenders = RepositoryLayout.Libraries
            .Select(name => RepositoryLayout.SourceProjects[name])
            .Concat(RepositoryLayout.TestProjects.Values)
            .Where(project => project.ProjectReferences.Intersect(
                RepositoryLayout.Helpers, StringComparer.Ordinal).Any())
            .Select(project => project.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Nothing_else_lives_in_src()
    {
        var expected = RepositoryLayout.Libraries
            .Concat(RepositoryLayout.WindowsBoundLibraries)
            .Concat(RepositoryLayout.Applications)
            .Concat(RepositoryLayout.Helpers)
            .OrderBy(name => name, StringComparer.Ordinal);

        var actual = RepositoryLayout.SourceProjects.Keys
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// No test project may depend on Control or Display: the Linux job does not build them,
    /// and a test tree that needs them would fail there for a reason nobody would look for
    /// in the test tree (Part 2, Part 11).
    /// </summary>
    [Fact]
    public void No_test_project_depends_on_an_application()
    {
        var offenders = RepositoryLayout.TestProjects.Values
            .Where(project => project.ProjectReferences.Intersect(
                RepositoryLayout.Applications, StringComparer.Ordinal).Any())
            .Select(project => project.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Every project below src/ and tests/ is in the solution. Without this a new project
    /// would simply not be built - and nothing would say so, because a solution that does not
    /// know a project cannot fail on it.
    /// </summary>
    [Fact]
    public void Every_project_is_in_the_solution()
    {
        var solution = ReadSolution();

        var missing = RepositoryLayout.SourceProjects.Values
            .Concat(RepositoryLayout.TestProjects.Values)
            .Where(project => !solution.Contains(project.Name + ".csproj", StringComparison.Ordinal))
            .Select(project => project.Name)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// The Linux job builds through a solution filter, so the filter is what decides which
    /// projects are checked on the second platform. If a new library were missing from it,
    /// the job would stay green and simply stop looking - the quietest way for a net to fail.
    /// The rule is therefore: the filter carries everything the solution carries, except what is
    /// Windows-bound - the applications, the platform project and the two firewall helpers
    /// (Part 2).
    /// </summary>
    [Fact]
    public void The_Linux_filter_covers_every_platform_neutral_project()
    {
        var filter = File.ReadAllText(
            Path.Combine(RepositoryLayout.RepositoryRoot.FullName, "DnDOverlay.Libraries.slnf"));

        var shouldBeCovered = RepositoryLayout.SourceProjects.Values
            .Where(project => !RepositoryLayout.WindowsBound.Contains(project.Name, StringComparer.Ordinal))
            .Concat(RepositoryLayout.TestProjects.Values
                .Where(project => !RepositoryLayout.WindowsBoundTests.Contains(project.Name, StringComparer.Ordinal)))
            .Select(project => project.Name)
            .ToList();

        var missing = shouldBeCovered
            .Where(name => !filter.Contains(name + ".csproj", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(missing);

        // And the other way round: nothing Windows-bound may be in it - test projects included.
        // Those target net10.0-windows and fail on Linux with NETSDK1100 - measured, not assumed.
        foreach (var windowsBound in RepositoryLayout.WindowsBound.Concat(RepositoryLayout.WindowsBoundTests))
        {
            Assert.DoesNotContain(windowsBound + ".csproj", filter, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The installers are never built through the solution - and therefore they are not in it.
    /// A property of the layout instead of a setting somebody can undo (Part 2).
    /// </summary>
    [Fact]
    public void The_installers_are_not_part_of_the_solution()
    {
        var solution = ReadSolution();

        Assert.DoesNotContain("wixproj", solution, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("installer", solution, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSolution() =>
        File.ReadAllText(Path.Combine(RepositoryLayout.RepositoryRoot.FullName, "DnDOverlay.slnx"));

    /// <summary>
    /// The hub speaks HTTP and WebSocket, so it carries the ASP.NET Core framework reference -
    /// and it is the only library that does. Kestrel lives inside the Control process, which is
    /// why Control is published self-contained (Part 9).
    /// </summary>
    [Fact]
    public void Only_the_Hub_references_AspNetCore()
    {
        foreach (var project in RepositoryLayout.SourceProjects.Values)
        {
            var expected = project.Name == "DnDOverlay.Hub"
                ? new[] { "Microsoft.AspNetCore.App" }
                : [];

            Assert.Equal(expected, project.FrameworkReferences);
        }
    }
}
