using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DnDOverlay.Core;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// The stage: every screen as a tile, side by side, wrapping by width (Part 7).
/// <para>
/// <b>Wrapping rather than free positions</b>, and that is a decision rather than the easy way: an
/// arrangement laid out on a wide monitor would leave holes and tiles outside the visible area on
/// the surface, and it would need a second layout logic for a gain nobody notices at two to four
/// tiles.
/// </para>
/// <para>
/// <b>The active screen is where the next blind grip lands</b> - the paste hotkey is pressed from
/// inside MapTool, without the control being visible at all, so which tile is active has to be
/// readable at a glance (Part 7). Tapping a tile makes it active.
/// </para>
/// <para>
/// <b>The scenes come from the hub, never from what this window has just sent.</b> A second control
/// changes the same table, and a stage that trusted its own command would drift from it (rule 1).
/// </para>
/// </summary>
internal sealed class StageBoard : WrapPanel
{
    private readonly ISessionApi _session;
    private readonly Pictures _pictures;
    private readonly Dictionary<ScreenRef, ScreenTile> _tiles = [];
    private readonly Dictionary<ScreenRef, string> _labels = [];

    private IReadOnlyList<ScreenView> _screens = [];

    internal StageBoard(ISessionApi session, Pictures pictures)
    {
        _session = session;
        _pictures = pictures;

        Orientation = Orientation.Horizontal;
    }

    /// <summary>
    /// Which screen the next grip lands on. <see langword="null"/> only before the first screen is
    /// known - after that the stage always has one, because a blind grip must never have nowhere to
    /// go (Part 7).
    /// </summary>
    internal ScreenRef? Active { get; private set; }

    /// <summary>Raised when the DM has made another tile the active one.</summary>
    internal event EventHandler? ActiveChanged;

    /// <summary>
    /// The screens as the hub knows them. Tiles come and go with them - a screen that is unplugged
    /// keeps its scene in the hub, but there is nothing to show it on until it is back.
    /// </summary>
    internal void Show(IReadOnlyList<ScreenView> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        _screens = screens;

        foreach (var view in screens)
        {
            _labels[view.Screen] = view.Info.Label;

            if (_tiles.ContainsKey(view.Screen))
            {
                continue;
            }

            var tile = new ScreenTile(view.Screen, _session, _pictures);

            tile.PreviewMouseDown += (_, _) => Activate(view.Screen);
            tile.PreviewTouchDown += (_, _) => Activate(view.Screen);

            _tiles[view.Screen] = tile;
            Children.Add(tile);
        }

        foreach (var gone in _tiles.Keys.Where(screen => !screens.Any(view => view.Screen == screen)).ToList())
        {
            Children.Remove(_tiles[gone]);
            _tiles.Remove(gone);
        }

        if (Active is not { } active || !_tiles.ContainsKey(active))
        {
            Activate(screens.Count > 0 ? screens[0].Screen : null);
        }
    }

    /// <summary>
    /// Fetches every scene and hands it to its tile. Called on every patch: what a patch changed is
    /// known to the hub, and asking it is one round trip in the same process - cheaper than a
    /// second copy of the scene state here that could drift from it (rule 1).
    /// </summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        foreach (var view in _screens)
        {
            if (!_tiles.TryGetValue(view.Screen, out var tile))
            {
                continue;
            }

            var scene = await _session.GetSceneAsync(view.Screen, cancellationToken).ConfigureAwait(true);

            // Size and shape are all the drawing takes from the context: where the parked cards
            // lie, how large they are and which way they face is already computed into the items
            // by the hub (Parking.Arrange), so the tile does not need this screen's own parameters
            // to draw them in the right place.
            tile.Show(
                _labels.GetValueOrDefault(view.Screen, view.Info.Label),
                scene,
                ScreenContext.Default(view.Info.Size, view.Info.Dpi),
                ViewRotation.None);
        }
    }

    private void Activate(ScreenRef? screen)
    {
        if (Active == screen)
        {
            return;
        }

        Active = screen;

        foreach (var (reference, tile) in _tiles)
        {
            tile.Active = reference == screen;
        }

        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
