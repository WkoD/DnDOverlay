using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Scene;

namespace DnDOverlay.Core.Tests.Protocol;

/// <summary>
/// The operations on the wire. They are discriminated over a FIXED list of derived types rather
/// than over a transmitted type name, which is a security property and not an optimisation
/// (Part 4) - and a fixed list is exactly the kind that gets forgotten when the next one is added.
/// </summary>
public sealed class PatchOpJsonTests
{
    private static readonly ScreenRef Screen =
        new(new DeviceId(Guid.NewGuid()), new ScreenId("TISCH-PC//DISPLAY1"));

    private static readonly ItemId Item = new(Guid.NewGuid());

    /// <summary>One of each, and <see cref="Every_operation_is_covered_here"/> is what keeps it one of each.</summary>
    private static readonly PatchOp[] Operations =
    [
        new AddItem(Build.Item()),
        new RemoveItem(Item),
        new SetBackground(Build.Background()),
        new ClearBackground(),
        new SetName(Build.Asset(), "Ratsherr Vellin"),
        new SetShowName(Item, Show: true),
        new SetAnimationPaused(Item, Paused: true),
        new ToggleItems(Visible: false),
        new ToggleBackground(Visible: false),
        new TransformItem(Item, CenterX: 0.4, CenterY: 0.6, Scale: 0.35, RotationDeg: 270, ZOrder: 12, Revision: 48),
        new SetLocked(Item, Locked: true),
        new ParkItem(Item, Parked: true, ZOrder: 13, Revision: 49),
    ];

    public static TheoryData<PatchOp> AllOperations() => [.. Operations];

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void Every_operation_survives_the_round_trip(PatchOp op)
    {
        var message = new ScenePatchMessage(new ScenePatch([new ScreenOp(Screen, op)]));

        Assert.Equal(message, ProtocolJson.Parse(ProtocolJson.Serialise(message)));
    }

    /// <summary>
    /// The list above is hand-written, so this is what stops it drifting: a new operation that
    /// nobody added here would otherwise be untested AND unregistered, and the round trip would
    /// stay green by simply not knowing about it.
    /// </summary>
    [Fact]
    public void Every_operation_is_covered_here()
    {
        var declared = typeof(PatchOp).Assembly
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(PatchOp)))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        var covered = Operations
            .Select(op => op.GetType().Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(declared, covered);
    }

    /// <summary>
    /// Every operation is registered on the base type. Without this an unregistered one throws at
    /// serialisation time - at the table, in the middle of a patch, and only for the operation
    /// nobody happened to exercise.
    /// </summary>
    [Fact]
    public void Every_operation_is_registered_on_the_base_type()
    {
        var registered = typeof(PatchOp)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.DerivedType)
            .ToHashSet();

        var missing = typeof(PatchOp).Assembly
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(PatchOp)) && !registered.Contains(type))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// The discriminator is the contract: once given out it is never reused for something else
    /// (Part 4, rule 7). Spelled out here so that renaming a C# type cannot quietly rename the
    /// wire form with it.
    /// </summary>
    [Theory]
    [InlineData(typeof(AddItem), "addItem")]
    [InlineData(typeof(RemoveItem), "removeItem")]
    [InlineData(typeof(SetBackground), "setBackground")]
    [InlineData(typeof(ClearBackground), "clearBackground")]
    [InlineData(typeof(SetName), "setName")]
    [InlineData(typeof(SetShowName), "setShowName")]
    [InlineData(typeof(SetAnimationPaused), "setAnimationPaused")]
    [InlineData(typeof(ToggleItems), "toggleItems")]
    [InlineData(typeof(ToggleBackground), "toggleBackground")]
    public void The_wire_name_of_an_operation_is_the_one_promised(Type operation, string wireName)
    {
        var attribute = Assert.Single(
            typeof(PatchOp).GetCustomAttributes<JsonDerivedTypeAttribute>(),
            candidate => candidate.DerivedType == operation);

        Assert.Equal(wireName, attribute.TypeDiscriminator);

        // And it really is what goes down the wire - the attribute alone would only prove intent.
        var op = Operations.Single(candidate => candidate.GetType() == operation);
        var json = Encoding.UTF8.GetString(
            ProtocolJson.Serialise(new ScenePatchMessage(new ScenePatch([new ScreenOp(Screen, op)]))));

        Assert.Contains($"\"op\":\"{wireName}\"", json, StringComparison.Ordinal);
    }
}
