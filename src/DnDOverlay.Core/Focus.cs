namespace DnDOverlay.Core;

/// <summary>
/// The rules of a focus that the hub and the drawing share (Part 3).
/// </summary>
public static class Focus
{
    /// <summary>
    /// How many items a focus may hold. Above it it is no longer a highlight but a second scene, so
    /// the focus is refused and the tile says why (Part 3: "proposal six").
    /// </summary>
    public const int Limit = 6;
}
