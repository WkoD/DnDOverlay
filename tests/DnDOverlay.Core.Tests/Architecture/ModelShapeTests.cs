using System.Reflection;
using DnDOverlay.Hub;

namespace DnDOverlay.Core.Tests.Architecture;

/// <summary>
/// Three promises of the data model that hold by the SHAPE of a type rather than by anybody's
/// care, read by reflection over the five libraries.
/// <para>
/// They were listed at M0 as "bites from M1a, once there are types" and then carried from bogen
/// to bogen without being written, until the data model moved to <c>docs/data-model.md</c> on
/// 17.10.2026 and the sentences there needed something to stand on. Each check is a helper that
/// is also run against a deliberately wrong type, so a helper that could never fail would fail
/// here first.
/// </para>
/// </summary>
public sealed class ModelShapeTests
{
    private static readonly Assembly[] Libraries =
    [
        Assembly.Load("DnDOverlay.Core"),
        Assembly.Load("DnDOverlay.Hub"),
        Assembly.Load("DnDOverlay.Campaign"),
        Assembly.Load("DnDOverlay.Imaging"),
        Assembly.Load("DnDOverlay.Transport"),
    ];

    /// <summary>
    /// What a display reports about a screen carries no state. The state is the DM's wish, born in
    /// the control and travelling only outwards; a field that holds in one direction and must not
    /// be read in the other gets read eventually.
    /// </summary>
    [Fact]
    public void ScreenInfo_carries_no_state() =>
        Assert.Empty(StateMembers(typeof(ScreenInfo)));

    [Fact]
    public void The_state_check_finds_a_state_where_there_is_one() =>
        Assert.Equal(["State", "Wish"], StateMembers(typeof(ReportWithAState)));

    /// <summary>
    /// The command interface and the stream a surface reads address a screen by
    /// <see cref="ScreenRef"/> only. Two cloned display PCs can report the same
    /// <see cref="ScreenId"/>, so a bare one would mean either of two tables.
    /// <para>
    /// <b>Scoped to what a surface sees, and the scope is the rule.</b> Inside the hub a bare
    /// <see cref="ScreenId"/> is right where the device is already known - the screens one
    /// connection reports, the settings one device sent. What must never take one is the place a
    /// screen is ADDRESSED from outside.
    /// </para>
    /// </summary>
    [Fact]
    public void Surfaces_address_screens_by_ScreenRef_only()
    {
        var offending = BareScreenIds(typeof(ISessionApi))
            .Concat(typeof(SessionEvent).GetNestedTypes().SelectMany(BareScreenIds))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void The_address_check_finds_a_bare_ScreenId_where_there_is_one() =>
        Assert.Equal(
            ["Move(screen)", "Scene(screens)", "Target"],
            BareScreenIds(typeof(AddressedByScreenId)));

    /// <summary>
    /// The mapping from an item to its rectangle exists once, in <see cref="Layout"/>, and the
    /// table, the thumbnail and the hit test all ask it (rule 9). A second copy is exactly how two
    /// surfaces start to disagree about where a picture is.
    /// </summary>
    [Fact]
    public void ItemToRect_exists_exactly_once()
    {
        var found = Libraries
            .SelectMany(TypesOf)
            .SelectMany(type => type.GetMethods(Everything))
            .Where(method => method.Name == nameof(Layout.ItemToRect) && method.DeclaringType is not null)
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Distinct()
            .ToList();

        Assert.Equal([$"{typeof(Layout).FullName}.{nameof(Layout.ItemToRect)}"], found);
    }

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance
        | BindingFlags.DeclaredOnly;

    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }

    private static List<string> StateMembers(Type type) =>
        [
            .. type.GetProperties()
                .Where(property => Unwrapped(property.PropertyType) == typeof(ScreenState))
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal),
        ];

    private static List<string> BareScreenIds(Type type)
    {
        var parameters = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .SelectMany(method => method.GetParameters()
                .Where(parameter => Mentions(parameter.ParameterType))
                .Select(parameter => $"{method.Name}({parameter.Name})"));

        var properties = type.GetProperties()
            .Where(property => Mentions(property.PropertyType))
            .Select(property => property.Name);

        return [.. parameters.Concat(properties).Order(StringComparer.Ordinal)];
    }

    /// <summary>A <see cref="ScreenId"/> itself, nullable, or inside a generic such as a list.</summary>
    private static bool Mentions(Type type)
    {
        var plain = Unwrapped(type);

        return plain == typeof(ScreenId) || (plain.IsGenericType && plain.GetGenericArguments().Any(Mentions));
    }

    private static Type Unwrapped(Type type) =>
        Nullable.GetUnderlyingType(type) ?? (type.IsByRef ? type.GetElementType()! : type);

    private sealed record ReportWithAState(ScreenId ScreenId, ScreenState State, ScreenState? Wish, string Label);

    private abstract class AddressedByScreenId
    {
        public abstract ScreenId Target { get; }

        public abstract ScreenRef Fine { get; }

        public abstract Task Move(ScreenId screen, ScreenRef to);

        public abstract Task Scene(IReadOnlyList<ScreenId> screens);
    }
}
