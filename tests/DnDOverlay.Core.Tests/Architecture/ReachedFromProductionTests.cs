using Mono.Cecil;

namespace DnDOverlay.Core.Tests.Architecture;

/// <summary>
/// The rule that would have caught <c>TokenContainer</c>: <b>a type that only tests ever touch is
/// not finished, it is orphaned.</b>
/// <para>
/// <b>What it cost to be without it.</b> The <c>.rptok</c> unpacker was built in M2a, proved
/// against eight generated containers in two generations including the malicious ones, and called
/// by NOBODY. A dropped token would have gone into the stock as a ZIP. The checklist was allowed to
/// say "closed" and was right - the unit was closed, the seam was not - and it survived a whole
/// milestone, because a unit test asks whether a thing works and never whether anyone uses it. It
/// is the same shape that once let a client and a hub each pass against their own stand-in while
/// neither ever sent a token.
/// </para>
/// <para>
/// <b>Why it needs the applications.</b> "Used" has to mean used by something that ships, and for
/// half these types the only such caller is <c>Control</c> or <c>Display</c> - <c>PictureFetch</c>
/// among them. So this rule reads the application assemblies too, which is exactly what the other
/// architecture rules must NOT do (they run on Linux, where the applications are not built at all).
/// Hence it skips rather than fails when they are absent: the Linux job proves platform neutrality,
/// this one proves that the wiring exists, and neither can stand in for the other.
/// </para>
/// </summary>
public sealed class ReachedFromProductionTests
{
    /// <summary>
    /// Types that no production code references and that are still right where they are. Each one
    /// needs a REASON here, and that is the point of the list: it is short, it is read, and adding
    /// to it is a deliberate act rather than a silence.
    /// <para>
    /// It is empty, and it stays that way for as long as it can. Measured when this rule was
    /// written: across the whole repository it found exactly one type it could not see, and that
    /// turned out to be a limit of the CHECK rather than a case for an exception - see
    /// <see cref="Considered"/>.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> Excused = new(StringComparer.Ordinal);

    /// <summary>
    /// Every public type of the five libraries is touched by at least one shipping assembly.
    /// <para>
    /// Reference-level rather than call-level on purpose: a type that is constructed and then
    /// ignored would still pass. That is a coarser net - and it catches the case that actually
    /// happened, at a price that stays near zero.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_public_library_type_is_reached_from_something_that_ships()
    {
        var production = ProductionModules();

        if (production is null)
        {
            Assert.Skip(
                "The applications are not built next to this test - on Linux they cannot be. "
                + "This rule needs them, because for several library types the only shipping "
                + "caller is Control or Display.");

            return;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in production)
        {
            Collect(module, used);
        }

        var orphans = new List<string>();

        foreach (var name in RepositoryLayout.Libraries)
        {
            using var module = ModuleDefinition.ReadModule(Path.Combine(AppContext.BaseDirectory, name + ".dll"));

            orphans.AddRange(
                module.GetTypes()
                    .Where(Considered)
                    .Select(type => type.FullName)
                    .Where(full => !used.Contains(full) && !Excused.Contains(full))
                    .Select(full => $"{name}: {full} is referenced by tests only"));
        }

        // Named in full rather than counted: the whole value of this rule is the LIST, and a
        // failure that says "collection was not empty" would send the reader back to the code to
        // find out which type it meant.
        Assert.True(orphans.Count == 0, string.Join(Environment.NewLine, orphans));
    }

    /// <summary>
    /// What the rule is about: public types with behaviour. Nested types, compiler-generated ones
    /// and enums are left out - an enum is used through its VALUES, which carry the declaring type
    /// with them anyway, and a nested type is reached through its parent.
    /// <para>
    /// <b>And a type that holds nothing but constants, which is where measuring beat guessing.</b>
    /// The first run of this rule flagged exactly one type in the whole repository,
    /// <c>ConfigurationSchema</c> - and it is used, in a line as plain as
    /// <c>SchemaVersion = ConfigurationSchema.Version</c>. The compiler inlines a <c>const</c>, so
    /// the reference is gone before Cecil ever sees it. That is a limit of the CHECK, not a case
    /// for an excuse list: named there it would have read as "used somehow, we think", when the
    /// truth is that this kind of type is invisible to a reference check by construction.
    /// </para>
    /// </summary>
    private static bool Considered(TypeDefinition type) =>
        type.IsPublic
        && !type.IsNested
        && !type.IsEnum
        && !type.IsInterface
        && type.FullName != "<Module>"
        && !type.FullName.Contains('<', StringComparison.Ordinal)
        && !OnlyConstants(type)
        && !type.CustomAttributes.Any(attribute =>
            attribute.AttributeType.Name == "CompilerGeneratedAttribute");

    /// <summary>
    /// Nothing but <c>const</c> fields, and therefore nothing a reference check could ever see.
    /// </summary>
    private static bool OnlyConstants(TypeDefinition type) =>
        type.Fields.Count > 0
        && type.Fields.All(field => field.HasConstant)
        && type.Methods.All(method => method.IsConstructor)
        && type.Properties.Count == 0;

    /// <summary>
    /// Every type this module mentions, from wherever it mentions it: a field, a signature, a base
    /// type, an attribute, a generic argument or an instruction operand. A configuration document
    /// only ever named as <c>ConfigurationFile&lt;T&gt;</c>'s argument is used, and reading the
    /// instructions alone would call it an orphan.
    /// </summary>
    private static void Collect(ModuleDefinition module, HashSet<string> used)
    {
        foreach (var reference in module.GetTypeReferences())
        {
            Add(reference, used, owner: null);
        }

        foreach (var type in module.GetTypes())
        {
            // <b>The owner, and it is what makes this rule work at all.</b> Measured, not
            // foreseen: the first version counted every mention, so a type that called its own
            // private helpers declared ITSELF used - and the check passed cheerfully with the
            // unpacker unwired, which is the one case it was written for. A reference from a type
            // to itself says nothing about whether anyone needs the type.
            var owner = type.FullName;

            foreach (var method in type.Methods)
            {
                Add(method.ReturnType, used, owner);

                foreach (var parameter in method.Parameters)
                {
                    Add(parameter.ParameterType, used, owner);
                }

                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var variable in method.Body.Variables)
                {
                    Add(variable.VariableType, used, owner);
                }

                foreach (var operand in method.Body.Instructions.Select(instruction => instruction.Operand))
                {
                    switch (operand)
                    {
                        case TypeReference referenced:
                            Add(referenced, used, owner);
                            break;

                        case MethodReference called:
                            Add(called.DeclaringType, used, owner);
                            Add(called.ReturnType, used, owner);

                            // A type handed to a GENERIC METHOD, which is how half the wiring of
                            // this program is written: AddHostedService<DiscoveryBeacon>() names
                            // the beacon nowhere else. Missing this called the beacon an orphan on
                            // the first run - the check's second hole, found the same way as the
                            // first, by staging a case whose answer was known.
                            if (called is GenericInstanceMethod instantiated)
                            {
                                foreach (var argument in instantiated.GenericArguments)
                                {
                                    Add(argument, used, owner);
                                }
                            }

                            break;

                        case FieldReference field:
                            Add(field.DeclaringType, used, owner);
                            Add(field.FieldType, used, owner);
                            break;

                        default:
                            break;
                    }
                }
            }

            foreach (var field in type.Fields)
            {
                Add(field.FieldType, used, owner);
            }

            Add(type.BaseType, used, owner);

            foreach (var implemented in type.Interfaces)
            {
                Add(implemented.InterfaceType, used, owner);
            }
        }
    }

    /// <summary>
    /// Adds a type and everything inside it: a <c>List&lt;AssetRef&gt;</c> uses <c>AssetRef</c>,
    /// and an array of them does too.
    /// </summary>
    /// <param name="owner">
    /// The type the mention was found in, or <c>null</c> for a module-level reference. A mention of
    /// itself does not count as use.
    /// </param>
    private static void Add(TypeReference? reference, HashSet<string> used, string? owner)
    {
        switch (reference)
        {
            case null:
                return;

            case GenericInstanceType generic:
                Add(generic.ElementType, used, owner);

                foreach (var argument in generic.GenericArguments)
                {
                    Add(argument, used, owner);
                }

                return;

            case TypeSpecification specification:
                Add(specification.ElementType, used, owner);
                return;

            default:
                // Nested types count for their outermost parent: a private helper class inside a
                // type is not a second user of it.
                var name = Outermost(reference.FullName);

                if (owner is null || name != Outermost(owner))
                {
                    used.Add(name);
                }

                return;
        }
    }

    private static string Outermost(string fullName)
    {
        var nested = fullName.IndexOf('/', StringComparison.Ordinal);

        return nested < 0 ? fullName : fullName[..nested];
    }

    /// <summary>
    /// The assemblies that ship: the five libraries, the two Windows-bound ones, and the two
    /// applications. The applications are not next to this test - nothing references them, which is
    /// the whole point of them - so they are read out of their own build output, and their absence
    /// means this rule cannot be answered rather than that it passed.
    /// </summary>
    private static List<ModuleDefinition>? ProductionModules()
    {
        var modules = new List<ModuleDefinition>();

        foreach (var name in RepositoryLayout.Libraries.Concat(RepositoryLayout.WindowsBoundLibraries))
        {
            var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");

            if (File.Exists(path))
            {
                modules.Add(ModuleDefinition.ReadModule(path));
            }
        }

        foreach (var name in RepositoryLayout.Applications)
        {
            if (Built(name) is not { } path)
            {
                foreach (var module in modules)
                {
                    module.Dispose();
                }

                return null;
            }

            modules.Add(ModuleDefinition.ReadModule(path));
        }

        return modules;
    }

    /// <summary>
    /// The newest build output of an application, whatever configuration it was built in - and it
    /// has to be NEWER than the application's own sources.
    /// <para>
    /// <b>The freshness check is not caution, it is the same rule as <c>C11</c> in mechanical
    /// form,</b> and it was paid for twice while this test was being written: this is the only rule
    /// that reads assemblies nothing depends on, so nothing rebuilds them for it. Both times a
    /// staged case answered out of a stale DLL - once passing when it should have failed, once the
    /// other way round. A wrong answer from an old file is worse than no answer, so an old file is
    /// a loud failure now.
    /// </para>
    /// </summary>
    private static string? Built(string project)
    {
        var source = new DirectoryInfo(Path.Combine(RepositoryLayout.RepositoryRoot.FullName, "src", project));
        var bin = new DirectoryInfo(Path.Combine(source.FullName, "bin"));

        if (!bin.Exists)
        {
            return null;
        }

        var assembly = bin
            .EnumerateFiles(project + ".dll", SearchOption.AllDirectories)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        if (assembly is null)
        {
            return null;
        }

        var newestSource = source
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Max(file => (DateTime?)file.LastWriteTimeUtc);

        Assert.False(
            newestSource > assembly.LastWriteTimeUtc,
            $"{project} was changed after it was last built ({assembly.FullName}). This rule reads "
            + "that assembly, so an answer out of it would be about code that is no longer there - "
            + "build the solution and run again.");

        return assembly.FullName;
    }
}
