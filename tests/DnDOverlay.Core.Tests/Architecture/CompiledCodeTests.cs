using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DnDOverlay.Core.Tests.Architecture;

/// <summary>
/// Rules about the compiled code of the five libraries, read from the IL with Cecil.
/// These are the ones a reference check cannot see: <c>DateTime.Now</c> and
/// <c>Environment.GetFolderPath</c> live in System.Runtime, an assembly that is allowed
/// anyway - only the CALL gives them away.
/// <para>
/// The Linux job catches none of this either, which is the reason these rules exist:
/// <c>Environment.GetFolderPath(LocalApplicationData)</c> happily returns
/// <c>~/.local/share</c> over there. It does not crash, it quietly goes somewhere else
/// (Part 2, Part 11).
/// </para>
/// </summary>
public sealed class CompiledCodeTests
{
    /// <summary>
    /// Time comes from the injected <c>TimeProvider</c> (rule 10). Without it half of the
    /// checklist in Part 11 stops being automatable - the doubled hour of the autumn clock
    /// change and the campaign opened in a different time zone cannot be staged otherwise.
    /// </summary>
    [Fact]
    public void No_library_reads_the_clock_directly()
    {
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.DateTime::get_Now",
            "System.DateTime::get_UtcNow",
            "System.DateTime::get_Today",
            "System.DateTimeOffset::get_Now",
            "System.DateTimeOffset::get_UtcNow",
        };

        AssertNoCallsTo(forbidden);
    }

    /// <summary>
    /// Storage paths are handed in, never hard wired (rule 10, Part 9). The default value
    /// %LOCALAPPDATA% is a line in the APPLICATION - the place where EnumDisplayMonitors and
    /// the registry live too, and the only place platform knowledge belongs.
    /// </summary>
    [Fact]
    public void No_library_determines_a_storage_path_on_its_own()
    {
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Environment::GetFolderPath",
            "System.IO.Path::GetTempPath",
        };

        AssertNoCallsTo(forbidden);
    }

    /// <summary>
    /// Platform APIs belong in the application. A P/Invoke compiles happily under net10.0 and
    /// only fails at run time on another system - it needs the rule more than a library does,
    /// not less (Part 1, rule 8). <c>[LibraryImport]</c> generates exactly such a method, so
    /// the same flag catches both.
    /// </summary>
    [Fact]
    public void No_library_calls_into_native_code()
    {
        var offenders = new List<string>();

        foreach (var module in LibraryModules())
        {
            foreach (var type in module.GetTypes())
            {
                offenders.AddRange(
                    type.Methods
                        .Where(method => method.IsPInvokeImpl)
                        .Select(method => $"{module.Assembly.Name.Name}: {type.FullName}.{method.Name}"));
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Nothing is ever unpacked onto disk, so there is no zip slip (Part 4, Part 5). The line
    /// runs between two assemblies: <c>ZipArchive</c> lives in System.IO.Compression and is
    /// allowed, <c>ExtractToFile</c> and <c>ExtractToDirectory</c> live in
    /// System.IO.Compression.ZipFile and are not. Reading yes, writing never.
    /// </summary>
    [Fact]
    public void No_library_writes_an_archive_entry_to_disk()
    {
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.IO.Compression.ZipFileExtensions::ExtractToFile",
            "System.IO.Compression.ZipFileExtensions::ExtractToDirectory",
            "System.IO.Compression.ZipFile::ExtractToDirectory",
        };

        AssertNoCallsTo(forbidden);
    }

    /// <summary>
    /// DPAPI is exactly the kind of foreign dependency rule 8 is made for, and pairing is the
    /// hub's business - so it would have slipped in here. It sits behind ISecretStore, and the
    /// implementation belongs to the application (Part 4).
    /// </summary>
    [Fact]
    public void No_library_uses_Windows_only_platform_types()
    {
        var offenders = new List<string>();

        foreach (var module in LibraryModules())
        {
            offenders.AddRange(
                module.AssemblyReferences
                    .Select(reference => reference.Name)
                    .Where(IsWindowsOnly)
                    .Select(name => $"{module.Assembly.Name.Name} references {name}"));
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Nothing reaches ImageMagick without passing the coder policy first (Part 5).
    /// <para>
    /// This is the half of the promise that can regress: somebody adds a method to the codec later
    /// and forgets the guard. Nothing fails - it works, the hardening is simply absent for that one
    /// path, and the URL import stops being separated from a remote control.
    /// </para>
    /// <para>
    /// The other half - that a policy applied too LATE is caught - cannot be asserted from IL and
    /// is not tried here: <c>CoderPolicy.Apply</c> proves its own effect by touching a denied coder
    /// (Part 5). And the end-to-end case, a process that never applies it at all, needs a second
    /// process; it is due with the codec's start-up path in M2b.
    /// </para>
    /// </summary>
    [Fact]
    public void Nothing_reaches_ImageMagick_without_passing_the_coder_policy()
    {
        const string Imaging = "DnDOverlay.Imaging";
        const string Gate = "DnDOverlay.Imaging.CoderPolicy::EnsureApplied";

        using var module = ModuleDefinition.ReadModule(
            Path.Combine(AppContext.BaseDirectory, Imaging + ".dll"));

        var methods = module.GetTypes().SelectMany(type => type.Methods).ToList();

        // A renamed gate would turn this whole rule into a no-op that passes for ever. It has to
        // be there before anything is concluded from its absence (Part 11, "a test that never
        // failed is unproven").
        Assert.Contains(
            methods,
            method => $"{method.DeclaringType.FullName}::{method.Name}" == Gate);

        var offenders = new List<string>();

        foreach (var type in module.GetTypes().Where(type => type.FullName != "DnDOverlay.Imaging.CoderPolicy"))
        {
            foreach (var method in type.Methods.Where(method => method.IsPublic && method.HasBody))
            {
                var reached = Reachable(method);

                if (reached.Any(TouchesImageMagick) && !reached.Contains(Gate, StringComparer.Ordinal))
                {
                    offenders.Add($"{type.FullName}.{method.Name} reaches ImageMagick without {Gate}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static bool TouchesImageMagick(string member) =>
        member.StartsWith("ImageMagick.", StringComparison.Ordinal);

    /// <summary>
    /// Every member a method can reach without leaving this assembly. Following the calls rather
    /// than looking at one body matters: a public method that hands the work to a private helper
    /// would otherwise look innocent, and that is the shape the code naturally takes.
    /// </summary>
    private static HashSet<string> Reachable(MethodDefinition entry)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<MethodDefinition>();
        var pending = new Queue<MethodDefinition>();

        pending.Enqueue(entry);
        seen.Add(entry);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            if (!current.HasBody)
            {
                continue;
            }

            foreach (var called in current.Body.Instructions
                         .Select(instruction => instruction.Operand)
                         .OfType<MethodReference>())
            {
                reached.Add($"{called.DeclaringType.FullName}::{called.Name}");

                // Resolving only succeeds inside this assembly, which is exactly the boundary we
                // want: foreign code is a leaf, ours is followed.
                var definition = called as MethodDefinition
                                 ?? (called.DeclaringType.Scope == entry.Module ? called.Resolve() : null);

                if (definition is not null && definition.Module == entry.Module && seen.Add(definition))
                {
                    pending.Enqueue(definition);
                }
            }
        }

        return reached;
    }

    private static bool IsWindowsOnly(string assemblyName) =>
        assemblyName.StartsWith("Microsoft.Win32.Registry", StringComparison.Ordinal)
        || assemblyName.StartsWith("System.Security.Cryptography.ProtectedData", StringComparison.Ordinal)
        || assemblyName.StartsWith("System.Drawing.Common", StringComparison.Ordinal)
        || assemblyName.StartsWith("PresentationCore", StringComparison.Ordinal)
        || assemblyName.StartsWith("PresentationFramework", StringComparison.Ordinal)
        || assemblyName.StartsWith("WindowsBase", StringComparison.Ordinal);

    private static void AssertNoCallsTo(HashSet<string> forbiddenMembers)
    {
        var offenders = new List<string>();

        foreach (var module in LibraryModules())
        {
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    offenders.AddRange(
                        method.Body.Instructions
                            .Select(instruction => instruction.Operand)
                            .OfType<MethodReference>()
                            .Select(called => $"{called.DeclaringType.FullName}::{called.Name}")
                            .Where(forbiddenMembers.Contains)
                            .Select(called => $"{module.Assembly.Name.Name}: {type.FullName}.{method.Name} calls {called}"));
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Only the five library assemblies are inspected, never the two applications - and that
    /// is a condition, not a description: on Linux, Control and Display are not built at all.
    /// A test that collects "everything under src/" would pass here and fall over there
    /// (Part 11). The applications may do everything the libraries may not.
    /// </summary>
    private static IEnumerable<ModuleDefinition> LibraryModules()
    {
        foreach (var name in RepositoryLayout.Libraries)
        {
            var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            Assert.True(File.Exists(path), $"{name}.dll is missing next to the test assembly.");

            using var module = ModuleDefinition.ReadModule(path);
            yield return module;
        }
    }
}
