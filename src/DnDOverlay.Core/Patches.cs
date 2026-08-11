using System.Text.Json.Serialization;

namespace DnDOverlay.Core;

/// <summary>
/// One operation on one scene. The base is discriminated over a FIXED list of derived types -
/// never over a transmitted type name, which is the same stance the whole protocol takes
/// (Part 4).
/// <para>
/// Only the operations a milestone actually reduces live here. The remaining thirteen from
/// Part 4 arrive with the milestone that implements them: rule 7 is additive, and an operation
/// that serialises but does nothing in the reducer would look implemented while being a trap.
/// Discriminator strings, once given out, are never reused for something else.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(AddItem), "addItem")]
public abstract record PatchOp;

/// <summary>
/// Puts an item on a screen. The hub has already done the placement, the capping and the
/// <c>ZOrder</c>; what travels is the finished item (Part 1, rule 2).
/// </summary>
public sealed record AddItem(SceneItem Item) : PatchOp;

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
