using System.Text.Json.Serialization;

namespace DnDOverlay.Core;

/// <summary>
/// One operation on one scene. The base is discriminated over a FIXED list of derived types -
/// never over a transmitted type name, which is the same stance the whole protocol takes
/// (Part 4).
/// <para>
/// Only the operations a milestone actually reduces live here. The remaining five of Part 4's
/// fourteen - <c>TransformItem</c>, <c>SetLocked</c> and <c>ParkItem</c> with the gestures in M3,
/// <c>SetFocus</c> with them, <c>ClearItems</c> with "empty the lot" in M5b - arrive with the
/// milestone that implements them: rule 7 is additive, and an operation that serialises but does
/// nothing in the reducer would look implemented while being a trap. Discriminator strings, once
/// given out, are never reused for something else.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(AddItem), "addItem")]
[JsonDerivedType(typeof(RemoveItem), "removeItem")]
[JsonDerivedType(typeof(SetBackground), "setBackground")]
[JsonDerivedType(typeof(ClearBackground), "clearBackground")]
[JsonDerivedType(typeof(SetName), "setName")]
[JsonDerivedType(typeof(SetShowName), "setShowName")]
[JsonDerivedType(typeof(SetAnimationPaused), "setAnimationPaused")]
[JsonDerivedType(typeof(ToggleItems), "toggleItems")]
[JsonDerivedType(typeof(ToggleBackground), "toggleBackground")]
public abstract record PatchOp;

/// <summary>
/// Puts an item on a screen. The hub has already done the placement, the capping and the
/// <c>ZOrder</c>; what travels is the finished item (Part 1, rule 2).
/// </summary>
public sealed record AddItem(SceneItem Item) : PatchOp;

/// <summary>
/// Takes one item off a screen. An <see cref="ItemId"/> the scene does not carry leaves it
/// unchanged - that is a promise of its own (Part 11), because a patch may legitimately arrive
/// twice or late.
/// </summary>
public sealed record RemoveItem(ItemId Item) : PatchOp;

/// <summary>
/// Puts a picture on the background layer, replacing whatever was there. Removing is
/// <see cref="ClearBackground"/> and not this operation with a null - the two are strictly
/// separate so that "empty the lot" has to say both out loud (Part 3).
/// </summary>
public sealed record SetBackground(BackgroundItem Background) : PatchOp;

/// <summary>Takes the background layer away. Leaves the items alone.</summary>
public sealed record ClearBackground : PatchOp;

/// <summary>
/// Renames an ASSET, not an item: every item of this scene carrying that
/// <see cref="Core.AssetId"/>, and the background too when it shows the same picture.
/// <para>
/// That is what keeps "one picture, one name" (Part 3) enforceable with a single patch without
/// leading the reducer out of its own scene. The control sends one of these per affected screen,
/// otherwise the same picture would briefly be called two different things (Part 4).
/// <see cref="SetShowName"/> is the opposite: it means exactly one item.
/// </para>
/// </summary>
public sealed record SetName(AssetId Asset, string Name) : PatchOp;

/// <summary>
/// Whether this one item wears its caption. It belongs to the INSTANCE - the councillor everyone
/// knows carries his name badge, the stranger does not (Part 3).
/// </summary>
/// <param name="Item">
/// <see langword="null"/> means the background layer, which has the same field: a city map wants
/// to be able to carry its name (Part 7).
/// </param>
public sealed record SetShowName(ItemId? Item, bool Show) : PatchOp;

/// <summary>Holds one animation still, or lets it run again.</summary>
/// <param name="Item"><see langword="null"/> means the background layer.</param>
public sealed record SetAnimationPaused(ItemId? Item, bool Paused) : PatchOp;

/// <summary>
/// Whether the item layer of this screen is drawn. The picture stays where it is and the device
/// keeps it in its store, which is what makes fading back in immediate and free (Part 7).
/// <para>
/// <b>It carries the resulting value rather than flipping</b>, though it is named for the command
/// that produces it. A flip is not idempotent, and this protocol explicitly allows a patch to
/// arrive twice after a reconnect - the same reason <see cref="AddItem"/> replaces rather than
/// appends. Which of the two values it is remains the hub's decision (Part 1, rule 2).
/// </para>
/// </summary>
public sealed record ToggleItems(bool Visible) : PatchOp;

/// <summary>
/// Whether the background layer of this screen is drawn - independent of
/// <see cref="ToggleItems"/> in all four combinations (Part 11, step 24).
/// </summary>
public sealed record ToggleBackground(bool Visible) : PatchOp;

/// <summary>
/// One operation addressed at one screen. The address is always the full <see cref="ScreenRef"/>,
/// including on the wire - the connection to a display does say which device is meant, but
/// <c>/ws/control</c> does not: there the messages of ALL devices run over ONE connection, and a
/// bare <see cref="ScreenId"/> would be ambiguous, and on two cloned display PCs plainly wrong
/// (Part 4).
/// </summary>
public sealed record ScreenOp(ScreenRef Screen, PatchOp Op);

/// <summary>
/// What one command of the DM produces: exactly one patch, with as many operations as that one
/// command needs. Independent commands are never merged, not even in quick succession - five
/// separately inserted images are five patches with five revisions and five steps in the undo
/// timeline (Part 4).
/// <para>
/// The patch is part of the MODEL, not just of the protocol: the reducer takes a
/// <see cref="PatchOp"/>, and the undo timeline stores pairs of these (Part 3).
/// </para>
/// </summary>
public sealed record ScenePatch(IReadOnlyList<ScreenOp> Ops)
{
    public bool Equals(ScenePatch? other) =>
        other is not null && Ops.SequenceEqual(other.Ops);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var op in Ops)
        {
            hash.Add(op);
        }

        return hash.ToHashCode();
    }
}
