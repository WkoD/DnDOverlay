using DnDOverlay.Core;

namespace DnDOverlay.Hub;

/// <summary>
/// The one place the authoritative state is changed, and at the same time the definition of what
/// <c>/ws/control</c> will translate (Part 4).
/// <para>
/// The line runs at the SCENE STATE, and it is narrower than it looks. What belongs here is
/// solely what changes or reads the authoritative arrangement. What does not: everything only
/// the DM sees - view rotation, tile order, hotkeys - and the entire stock, which belongs to
/// Campaign and is called by the control directly. That is why there is no <c>AddAsset</c> and
/// no <c>OpenCampaign</c> in here.
/// </para>
/// <para>
/// M1a implements the two members the running thread needs. The rest of the surface from Part 4
/// arrives with the milestones that serve it.
/// </para>
/// </summary>
public interface ISessionApi
{
    /// <summary>
    /// Puts an image on a screen. Placement, the width cap and the <c>ZOrder</c> happen HERE,
    /// not in the caller: placement means reading the state and writing it in the same breath,
    /// and two callers doing it at once would lay two images in the same slot (Part 3).
    /// </summary>
    /// <param name="position">
    /// An aimed drop point wins over the placement mode. <see langword="null"/> means "you
    /// decide".
    /// </param>
    Task<ItemId> AddItemAsync(
        ScreenRef screen,
        AssetRef asset,
        Point? position,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a scene - what "save screen as scene" will use.</summary>
    Task<SceneState> GetSceneAsync(ScreenRef screen, CancellationToken cancellationToken = default);
}
