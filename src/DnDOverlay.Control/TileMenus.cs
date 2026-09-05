using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Core;
using DnDOverlay.Hub;
using CoreManipulation = DnDOverlay.Core.Manipulation;
using CorePoint = DnDOverlay.Core.Point;
using TilePoint = System.Windows.Point;

namespace DnDOverlay.Control;

/// <summary>
/// The two context menus of a tile: the screen's and one picture's.
/// <para>
/// <b>They carry the rarer and the more final thing</b> - what is wanted often has a button of its
/// own (Part 7). Both are ordered the same way: by how often it is needed, and at the bottom,
/// behind a separator, whatever removes something. A slip at the lower edge then never costs a
/// picture somebody is looking at.
/// </para>
/// <para>
/// <b>They are built incomplete, and that is the order of work rather than a gap.</b> Six entries
/// Part 7 draws have no caller in this milestone - "show in the stock", "rename", "reset item",
/// "diagnostics", "save screen as scene" and "remove all images", the last of which has no
/// operation at all yet (<c>ClearItems</c> is M5b). <b>An entry that is there and does nothing is
/// worse than one that is missing</b>: it reads as built. What is here has a caller; the rest
/// arrives with what it does, in the places Part 7 already gives them, so nothing has to be
/// resorted later.
/// </para>
/// <para>
/// <b>The item menu acts on the SELECTION</b> when the picture it was opened on belongs to it, and
/// on that one picture otherwise (Part 7). Locking, parking or captioning four mercenaries is one
/// grip rather than four.
/// </para>
/// </summary>
internal sealed class TileMenus(
    ScreenRef screen,
    ISessionApi session,
    Func<IReadOnlyList<ScreenView>> targets)
{
    /// <summary>The DM asked for this screen on its own.</summary>
    internal event EventHandler? Opening;

    /// <summary>The DM asked for the window <i>Devices</i>, with this screen to hand.</summary>
    internal event EventHandler? Configuring;

    /// <summary>The DM turned the view of this screen.</summary>
    internal event EventHandler<ViewRotation>? Turning;

    /// <summary>The DM switched the background mode on or off.</summary>
    internal event EventHandler<bool>? Adjusting;

    /// <summary>
    /// The screen's own menu. Reached on free tile area <b>and on the head</b> - the second place
    /// is not convenience: on a full screen there is no free area left, and the menu would be out
    /// of reach exactly where most is going on (Part 7).
    /// </summary>
    internal void ForScreen(
        UIElement over, TilePoint at, SceneState scene, ViewRotation view, bool adjusting)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var menu = new ContextMenu();

        menu.Items.Add(Entry("Open single view", () => Opening?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(Entry("Set up screen ...", () => Configuring?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(Entry(
            "Identify screens",
            () => _ = session.IdentifyScreensAsync(screen.Device, CancellationToken.None)));

        // Switched here rather than on the picture, because the background layer takes no hits at
        // all and has no item to take hold of (Part 6).
        var named = Entry(
            scene.Background is { ShowName: true } ? "Hide background name" : "Show background name",
            () => _ = session.SetShowNameAsync(
                screen, item: null, !(scene.Background?.ShowName ?? false), CancellationToken.None));

        named.IsEnabled = scene.Background is not null;
        menu.Items.Add(named);

        // The two starting values, as the two buttons they became when the background took on the
        // place and size of a picture (Ortsfrage 6): each of them works the centre and the scale
        // out once, and from then on they are ordinary values that a grip can change. Flat rather
        // than in a submenu - "turn view" is the one place in this surface that nests (Part 7).
        menu.Items.Add(Fitted("Fill screen with background", BackgroundFit.Cover, scene));
        menu.Items.Add(Fitted("Fit whole background on screen", BackgroundFit.Contain, scene));

        // The one mode on the stage, and it is here because every grip on a tile is already spoken
        // for and the background has no item to take hold of (Part 6). Ticked while it lasts, like
        // the view rotation and the diagnostic view - the two other things one sets up once.
        var adjust = new MenuItem
        {
            Header = "Adjust background",
            IsCheckable = true,
            IsChecked = adjusting,
            IsEnabled = scene.Background is not null,
        };

        adjust.Click += (_, _) => Adjusting?.Invoke(this, !adjusting);

        menu.Items.Add(adjust);

        menu.Items.Add(Turned(view));

        menu.Items.Add(new Separator());

        var cleared = Entry(
            "Remove background",
            () => _ = session.ClearBackgroundAsync(screen, CancellationToken.None));

        cleared.IsEnabled = scene.Background is not null;
        menu.Items.Add(cleared);

        Open(menu, over, at);
    }

    /// <summary>One picture's menu, and through it the whole selection when it is part of one.</summary>
    internal void ForItem(
        UIElement over,
        TilePoint at,
        SceneState scene,
        SceneItem picture,
        ScreenContext context,
        Selection selection,
        CorePoint where)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selection);

        var many = selection.Contains(picture.ItemId)
            ? [.. scene.Items.Where(item => selection.Contains(item.ItemId))]
            : new List<SceneItem> { picture };

        var menu = new ContextMenu();

        menu.Items.Add(Entry("Bring to front", () => Each(many, item => Front(item))));
        menu.Items.Add(Entry("Turn to me", () => Each(many, item => ToMe(item, context, where))));
        menu.Items.Add(Entry(
            picture.Parked ? "Unpark" : "Park",
            () => Each(many, item => session.ParkItemAsync(
                screen, item.ItemId, !picture.Parked, CancellationToken.None))));

        menu.Items.Add(new Separator());

        menu.Items.Add(Entry(
            picture.Locked ? "Unlock" : "Lock",
            () => Each(many, item => session.SetLockedAsync(
                screen, item.ItemId, !picture.Locked, CancellationToken.None))));

        // Offered only where there is an animation to stop - a greyed entry on every still picture
        // would be four fifths of the menu saying no (Part 7).
        if (picture is ImageItem { Meta.IsAnimated: true } animated)
        {
            menu.Items.Add(Entry(
                animated.AnimationPaused ? "Resume animation" : "Pause animation",
                () => Each(many, item => session.SetAnimationPausedAsync(
                    screen, item.ItemId, !animated.AnimationPaused, CancellationToken.None))));
        }

        menu.Items.Add(Entry(
            picture is ImageItem { ShowName: true } ? "Hide name" : "Show name",
            () => Each(many, item => session.SetShowNameAsync(
                screen,
                item.ItemId,
                picture is not ImageItem { ShowName: true },
                CancellationToken.None))));

        menu.Items.Add(new Separator());

        menu.Items.Add(Onto("Copy to", (target, item) =>
            session.CopyItemAsync(screen, target, item.ItemId, position: null, CancellationToken.None), many));
        menu.Items.Add(Onto("Move to", (target, item) =>
            session.MoveItemAsync(screen, target, item.ItemId, position: null, CancellationToken.None), many));

        // Set apart, and expressly NOT beside "move to": in the menu they are neighbours, in effect
        // they are opposites, and a slip there clears a picture off the table (Part 7).
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(
            "Remove",
            () => Each(many, item => session.RemoveItemAsync(screen, item.ItemId, CancellationToken.None))));

        Open(menu, over, at);
    }

    /// <summary>
    /// One of the two fit buttons. <b>Not a stored mode any more</b>: it computes a place and a
    /// size, writes them, and is finished - which is what lets a background be moved and zoomed at
    /// all (Part 6, decided at the start of M4).
    /// </summary>
    private MenuItem Fitted(string header, BackgroundFit fit, SceneState scene)
    {
        var entry = Entry(
            header,
            () => _ = session.SetBackgroundFitAsync(screen, fit, CancellationToken.None));

        entry.IsEnabled = scene.Background is not null;

        return entry;
    }

    /// <summary>
    /// The one submenu in the whole surface, and the one level of nesting: four equal values laid
    /// out flat would take up half the menu, and anything deeper cannot be hit with a finger
    /// (Part 7).
    /// <para>
    /// <b>A button showing the angle is not missed - the turned thumbnail IS the display.</b> Only
    /// on an empty or point-symmetrically covered screen are 0 and 180 degrees indistinguishable,
    /// and that is what the tick is for.
    /// </para>
    /// </summary>
    private MenuItem Turned(ViewRotation view)
    {
        var turning = new MenuItem { Header = "Turn view" };

        foreach (var choice in Enum.GetValues<ViewRotation>())
        {
            var entry = new MenuItem
            {
                Header = $"{(int)choice}°",
                IsCheckable = true,
                IsChecked = choice == view,
            };

            entry.Click += (_, _) => Turning?.Invoke(this, choice);

            turning.Items.Add(entry);
        }

        return turning;
    }

    /// <summary>
    /// A target list over the screens, this one included: copying onto the same screen is what
    /// puts a second guard on the table, and moving onto it does nothing on purpose (Part 4).
    /// </summary>
    private MenuItem Onto(string header, Func<ScreenRef, SceneItem, Task> what, IReadOnlyList<SceneItem> many)
    {
        var entry = new MenuItem { Header = header };

        foreach (var view in targets())
        {
            var target = view.Screen;
            var choice = new MenuItem { Header = view.Info.Label };

            choice.Click += (_, _) => Each(many, item => what(target, item));

            entry.Items.Add(choice);
        }

        entry.IsEnabled = entry.Items.Count > 0;

        return entry;
    }

    /// <summary>
    /// Brings a picture to the front by reporting where it already lies as a GRAB. There is no
    /// operation of its own for it, and there must not be: what is taken hold of comes to the
    /// front is one rule, and the hub is where it is applied - together with the exception that a
    /// locked picture never rises (Part 3).
    /// </summary>
    private Task Front(SceneItem item) =>
        session.TransformItemAsync(
            screen,
            new ItemTransform(item.ItemId, item.CenterX, item.CenterY, item.Scale, item.RotationDeg),
            fromTable: false,
            toFront: true,
            CancellationToken.None);

    /// <summary>
    /// The same thing the double tap does, and the same arithmetic: the edge nearest the place the
    /// menu was opened on (Part 6).
    /// </summary>
    private Task ToMe(SceneItem item, ScreenContext context, CorePoint where)
    {
        var turned = CoreManipulation.HoldAtEdge(
            item with { RotationDeg = CoreManipulation.TurnToMe(where, context) },
            context);

        return session.TransformItemAsync(
            screen,
            new ItemTransform(item.ItemId, turned.CenterX, turned.CenterY, turned.Scale, turned.RotationDeg),
            fromTable: false,
            toFront: false,
            CancellationToken.None);
    }

    /// <summary>
    /// Runs one command over everything the menu acts on. <b>The label was decided from the picture
    /// that was hit</b>, so the whole selection follows that one - four mercenaries of which one is
    /// unlocked all end up locked, which is what "lock" on that menu said.
    /// </summary>
    private static void Each(IReadOnlyList<SceneItem> many, Func<SceneItem, Task> what)
    {
        foreach (var item in many)
        {
            _ = what(item);
        }
    }

    private static MenuItem Entry(string header, Action what)
    {
        var entry = new MenuItem { Header = header };

        entry.Click += (_, _) => what();

        return entry;
    }

    /// <summary>
    /// Opens the menu where the hand is rather than where the element is: a menu at the corner of a
    /// tile would make the DM look for what he had just pointed at.
    /// </summary>
    private static void Open(ContextMenu menu, UIElement over, TilePoint at)
    {
        menu.PlacementTarget = over;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        menu.HorizontalOffset = at.X;
        menu.VerticalOffset = at.Y;
        menu.IsOpen = true;
    }
}
