using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Arch = ArchUnitNET.Domain.Architecture;

namespace DnDOverlay.Core.Tests.Architecture;

/// <summary>
/// Dependency direction at type level, checked with ArchUnitNET. This complements
/// <see cref="ProjectStructureTests"/> rather than repeating it: that one reads the DECLARED
/// references and bites while the projects are still empty, this one reads what the compiler
/// actually emitted - a type used through a transitive path shows up here and nowhere else.
/// <para>
/// While the libraries carry no types yet, every rule here is vacuous - and ArchUnitNET
/// rejects that by default ("the rule requires positive evaluation"). Hence
/// <c>WithoutRequiringPositiveResults</c>: the rules are written now so that M1a adds rules
/// instead of infrastructure (Part 10). What the escape hatch would normally cost - a typo in
/// an assembly name passing unnoticed forever - is covered next door:
/// <see cref="ProjectStructureTests.Nothing_else_lives_in_src"/> asserts the exact set of
/// projects, so a renamed assembly fails there.
/// </para>
/// </summary>
public sealed class TypeDependencyTests
{
    private static readonly Arch Architecture = new ArchLoader()
        .LoadAssemblies(
            System.Reflection.Assembly.Load("DnDOverlay.Core"),
            System.Reflection.Assembly.Load("DnDOverlay.Hub"),
            System.Reflection.Assembly.Load("DnDOverlay.Campaign"),
            System.Reflection.Assembly.Load("DnDOverlay.Imaging"),
            System.Reflection.Assembly.Load("DnDOverlay.Transport"))
        .Build();

    /// <summary>Core knows nobody - that is what makes it the place the rules live in.</summary>
    [Fact]
    public void Core_depends_on_no_other_library() =>
        Types().That().ResideInAssembly("DnDOverlay.Core")
            .Should().NotDependOnAny(
                Types().That().ResideInAssembly("DnDOverlay.Hub")
                    .Or().ResideInAssembly("DnDOverlay.Campaign")
                    .Or().ResideInAssembly("DnDOverlay.Imaging")
                    .Or().ResideInAssembly("DnDOverlay.Transport"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);

    /// <summary>
    /// The arrangement is the hub's, the material is the campaign's (Part 1, idea 3). The hub
    /// reaches assets only through IAssetSource and IAssetSink, both defined in Core.
    /// </summary>
    [Fact]
    public void Hub_does_not_depend_on_Campaign() =>
        Types().That().ResideInAssembly("DnDOverlay.Hub")
            .Should().NotDependOnAny(Types().That().ResideInAssembly("DnDOverlay.Campaign"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);

    [Fact]
    public void Campaign_does_not_depend_on_the_Hub() =>
        Types().That().ResideInAssembly("DnDOverlay.Campaign")
            .Should().NotDependOnAny(Types().That().ResideInAssembly("DnDOverlay.Hub"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);

    /// <summary>
    /// Magick.NET is reachable from Imaging alone. Transport is the one that matters here:
    /// two costed promises hang on it staying free of it (Part 2).
    /// </summary>
    [Fact]
    public void Only_Imaging_depends_on_Magick() =>
        Types().That().ResideInAssembly("DnDOverlay.Core")
            .Or().ResideInAssembly("DnDOverlay.Hub")
            .Or().ResideInAssembly("DnDOverlay.Campaign")
            .Or().ResideInAssembly("DnDOverlay.Transport")
            .Should().NotDependOnAnyTypesThat().ResideInNamespace("ImageMagick")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
}
