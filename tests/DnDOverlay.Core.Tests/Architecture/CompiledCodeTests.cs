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
