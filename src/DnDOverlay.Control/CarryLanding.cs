using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// What something carried out of the panel does where it is let go (Part 7: "drag goes both ways").
/// <para>
/// <b>A layout reaches the screens it names</b>, wherever over the stage it lands - it has no single
/// target, and off the stage it does nothing. Everything else needs a tile under the hand: a scene is
/// loaded onto it, the background strip takes exactly one picture (a background layer holds one), one
/// picture lands where it was let go, and several are ONE command that lays them side by side.
/// </para>
/// <para>
/// The window asks the stage where the hand is and carries the answer out; this decides.
/// </para>
/// </summary>
internal static class CarryLanding
{
    /// <summary>What the drop comes to.</summary>
    internal abstract record Landing
    {
        /// <summary>Let go where nothing takes it.</summary>
        internal sealed record Nothing : Landing;

        /// <summary>A saved layout, loaded onto the screens it names.</summary>
        internal sealed record Layout(string Name) : Landing;

        /// <summary>Something for one screen - which becomes the active one, where the next blind grip lands.</summary>
        internal abstract record OnScreen(ScreenRef Screen) : Landing;

        /// <summary>A saved scene, loaded onto the screen.</summary>
        internal sealed record Scene(ScreenRef Screen, string Name) : OnScreen(Screen);

        /// <summary>One picture as the screen's background.</summary>
        internal sealed record Background(ScreenRef Screen, AssetRef Asset) : OnScreen(Screen);

        /// <summary>Anything but exactly one picture onto the background strip - refused, and said.</summary>
        internal sealed record BackgroundRefused(ScreenRef Screen) : OnScreen(Screen);

        /// <summary>One picture, where it was let go.</summary>
        internal sealed record Picture(ScreenRef Screen, AssetRef Asset, Point? At) : OnScreen(Screen);

        /// <summary>Several pictures, laid side by side by one command.</summary>
        internal sealed record Pictures(ScreenRef Screen, IReadOnlyList<AssetRef> Assets) : OnScreen(Screen);
    }

    /// <summary>A load let go.</summary>
    /// <param name="overStage">For a layout: whether it was let go anywhere over the stage.</param>
    /// <param name="aim">For anything else: the tile under the hand, the place on its screen, and whether that is the background strip - or none.</param>
    internal static Landing Decide(CarryLoad load, bool overStage, (ScreenRef Screen, Point? At, bool Background)? aim)
    {
        ArgumentNullException.ThrowIfNull(load);

        if (load.Layout is { } layout)
        {
            return overStage ? new Landing.Layout(layout) : new Landing.Nothing();
        }

        if (aim is not { } target)
        {
            return new Landing.Nothing();
        }

        if (load.Scene is { } scene)
        {
            return new Landing.Scene(target.Screen, scene);
        }

        if (target.Background)
        {
            return load.Pictures.Count == 1
                ? new Landing.Background(target.Screen, load.Pictures[0])
                : new Landing.BackgroundRefused(target.Screen);
        }

        return load.Pictures.Count == 1
            ? new Landing.Picture(target.Screen, load.Pictures[0], target.At)
            : new Landing.Pictures(target.Screen, load.Pictures);
    }
}
