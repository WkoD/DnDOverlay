using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
/// <b>Every entry has a caller.</b> They arrived with what they do, milestone by milestone, in the
/// places Part 7 gives them - the last were "focus", "reset item" and "remove all pictures" (M5b8).
/// <b>An entry that is there and does nothing is worse than one that is missing</b>: it reads as
/// built.
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

    /// <summary>The DM switched the diagnostic view of this screen on or off.</summary>
    internal event EventHandler<bool>? Diagnosing;

    /// <summary>The DM asked to save this screen as a scene.</summary>
    internal event EventHandler? Saving;

    /// <summary>The DM asked to see this picture in the stock.</summary>
    internal event EventHandler<AssetId>? ShowingInStock;

    /// <summary>The DM asked to rename this picture - campaign-wide, the same act as from the stock.</summary>
    internal event EventHandler<AssetId>? Renaming;

    /// <summary>The DM asked for this picture as the focus - the tile sets it and says it if it is refused.</summary>
    internal event EventHandler<IReadOnlyList<ItemId>>? Focusing;

    /// <summary>
    /// The screen's own menu. Reached on free tile area <b>and on the head</b> - the second place
    /// is not convenience: on a full screen there is no free area left, and the menu would be out
    /// of reach exactly where most is going on (Part 7).
    /// </summary>
    internal void ForScreen(
        UIElement over, TilePoint at, SceneState scene, ViewRotation view, bool adjusting, bool single, bool diagnosing, PlacementMode placing)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var menu = new ContextMenu();

        // What it will DO, not where it is. The entry read "Open single view" while the single view
        // was already open, which is the one moment a reader needs it to say the opposite
        // (hand-run of M4, second run).
        menu.Items.Add(Entry(
            single ? Strings.TileMenuBackToOverview : Strings.TileMenuOpenSingle,
            () => Opening?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(Entry(Strings.TileMenuSetUpScreen, () => Configuring?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(Entry(
            Strings.TileMenuIdentifyScreens,
            () => _ = session.IdentifyScreensAsync(screen.Device, CancellationToken.None)));

        // Saved where the arrangement came into being; LOADED only from the tab "Scenes", where one
        // sees what one loads (Part 7).
        menu.Items.Add(Entry(Strings.TileMenuSaveAsScene, () => Saving?.Invoke(this, EventArgs.Empty)));

        menu.Items.Add(Turned(view));

        // How new pictures arrive on this screen - moved here from the M2 scaffold when the panel
        // took its place (M5b3); the screen's own setting, so it belongs to the screen's menu.
        var placement = new MenuItem { Header = Strings.TileMenuPlacement };

        foreach (var mode in Enum.GetValues<PlacementMode>())
        {
            var item = new MenuItem { Header = Placement(mode), IsCheckable = true, IsChecked = placing == mode };

            item.Click += (_, _) => _ = session.ApplyConfigAsync(
                screen.Device,
                new ConfigUpdate([new ScreenConfigUpdate(screen.Screen, new ScreenSettings(Placement: mode))]),
                CancellationToken.None);

            placement.Items.Add(item);
        }

        menu.Items.Add(placement);

        // Ticked while it stands, and reached from the head as well as the free area: the view
        // replaces the thumbnail, so there is no free area left to switch it off from (Part 7).
        var diagnosis = new MenuItem { Header = Strings.TileMenuDiagnosticView, IsCheckable = true, IsChecked = diagnosing };

        diagnosis.Click += (_, _) => Diagnosing?.Invoke(this, !diagnosing);
        menu.Items.Add(diagnosis);

        menu.Items.Add(new Separator());

        // <b>No entry for the background's name any more</b> (fourth hand-run, 15.09.2026). It was
        // drawn under the layer the way a picture's is, and a background reaches past the screen
        // on one axis by design - so the caption sat exactly where it could not be read. What the
        // DM wants instead is free text placed on the screen - a design of its own in M5b, because
        // it changes the model (Part 10).

        // <b>The three ways to adjust the background, under one head.</b> They used to stand flat
        // and apart - two buttons here, the mode three entries further down - which read as three
        // unrelated things when they are one question with three answers: by hand, or one of the
        // two obvious positions. Ordered as the DM asked for them (Handlauf M4, dritter Lauf).
        //
        // <b>Only the two positions carry a tick</b> (fourth hand-run, 15.09.2026): Fill and Fit are
        // the two special states worth seeing at a glance, and "Customize" is a mode, whose sign is
        // the golden frame on the tile. Picking either position ends that mode - moving the layer
        // on by hand after choosing where it goes would undo the choice the moment it was made.
        //
        // "Customize" rather than "by hand": it stands beside two names for what the machine does,
        // and the DM asked for the more technical word.
        var custom = new MenuItem { Header = Strings.TileMenuCustomize };

        custom.Click += (_, _) => Adjusting?.Invoke(this, !adjusting);

        var background = new MenuItem { Header = Strings.TileMenuAdjustBackground, IsEnabled = scene.Background is not null };

        background.Items.Add(custom);

        // A mode above, two positions below: two kinds of answer, and the line says so.
        background.Items.Add(new Separator());
        background.Items.Add(Fitted(Strings.TileMenuFillScreen, BackgroundFit.Cover, scene));
        background.Items.Add(Fitted(Strings.TileMenuFitWhole, BackgroundFit.Contain, scene));

        menu.Items.Add(background);

        // An entry of its own and not a third level under "Adjust background": nothing here nests
        // deeper than one submenu, because anything deeper cannot be hit with a finger (Part 7).
        // A filled or fitted layer is filled or fitted again at the new angle - one command in the
        // hub, so one patch (TurnBackgroundAsync).
        var turned = Quarters(
            Strings.TileMenuTurnBackground,
            scene.Background?.RotationDeg,
            angle => session.TurnBackgroundAsync(screen, angle, CancellationToken.None));

        turned.IsEnabled = scene.Background is not null;
        menu.Items.Add(turned);

        // Last, and on its own: it is the one entry here that throws something away.
        var cleared = Entry(
            Strings.TileMenuRemoveBackground,
            () => _ = session.ClearBackgroundAsync(screen, CancellationToken.None));

        cleared.IsEnabled = scene.Background is not null;

        // Beside it, the other entry that throws something away: every picture off this screen, the
        // background left where it is - one step on the timeline.
        var emptied = Entry(
            Strings.TileMenuRemoveAllPictures,
            () => _ = session.ClearItemsAsync(screen, CancellationToken.None));

        emptied.IsEnabled = scene.Items.Count > 0;

        // The background's animation, held or running - the one grip of the scaffold the tile did
        // not have yet. Only for a background that moves.
        if (scene.Background is { Meta.IsAnimated: true } moving)
        {
            menu.Items.Add(Entry(
                moving.AnimationPaused ? Strings.TileMenuRunBackgroundAnimation : Strings.TileMenuHoldBackgroundAnimation,
                () => _ = session.SetAnimationPausedAsync(screen, null, !moving.AnimationPaused, CancellationToken.None)));
        }

        menu.Items.Add(cleared);
        menu.Items.Add(emptied);

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

        menu.Items.Add(Entry(Strings.TileMenuTurnToMe, () => _ = ToMe(many, context, where)));

        // <b>Quarter turns, set and not added</b> (end of M4, 27.09.2026): "90°" means standing at
        // 90° to the screen, the same way "Turn view" means it, so choosing it twice changes nothing.
        // Offered only for what lies out, like "Park": the fan owns every card's angle, and a turn
        // given to a card there would be a second truth beside it.
        var lying = many.Where(item => !item.Parked).ToList();

        if (!picture.Parked)
        {
            menu.Items.Add(Quarters(
                Strings.TileMenuTurn,
                Shared(lying),
                angle => session.TransformItemsAsync(
                    screen,
                    [.. lying.Select(item => Squared(item, angle))],

                    // As with "Turn to me": turning is not taking hold of, so no depth is handed out.
                    toFront: false,
                    CancellationToken.None)));
        }

        // "Park" only, and only for what is lying out. Taking hold of a card in the thumbnail is
        // the way back out of the fan, exactly as at the table - and the entry that did it from
        // here dropped the picture under the topmost slot, where nothing could reach it (hand-run
        // of M4, Fächer). What the entry is still worth is a whole selection put away at once.
        if (!picture.Parked)
        {
            menu.Items.Add(Entry(
                Strings.TileMenuPark,
                () => _ = session.ParkItemsAsync(
                    screen, Ids(many), parked: true, CancellationToken.None)));
        }

        // Exactly this picture, not the selection: the button under the tile is the grip for a
        // group (Part 7).
        menu.Items.Add(Entry(Strings.TileMenuFocus, () => Focusing?.Invoke(this, [picture.ItemId])));

        // Back to what it would get arriving now - for the whole selection, like the entries around it.
        menu.Items.Add(Entry(
            Strings.TileMenuResetItem,
            () => _ = session.TransformItemsAsync(screen, Core.Placement.Reset(scene, many, context), toFront: false, CancellationToken.None)));

        menu.Items.Add(new Separator());

        menu.Items.Add(Entry(
            picture.Locked ? Strings.TileMenuUnlock : Strings.TileMenuLock,
            () => _ = session.SetItemsLockedAsync(
                screen, Ids(many), !picture.Locked, CancellationToken.None)));

        // Offered only where there is an animation to stop - a greyed entry on every still picture
        // would be four fifths of the menu saying no (Part 7).
        if (picture is ImageItem { Meta.IsAnimated: true } animated)
        {
            menu.Items.Add(Entry(
                animated.AnimationPaused ? Strings.TileMenuResumeAnimation : Strings.TileMenuPauseAnimation,
                () => _ = session.SetItemsAnimationPausedAsync(
                    screen, Ids(many), !animated.AnimationPaused, CancellationToken.None)));
        }

        menu.Items.Add(Entry(
            picture is ImageItem { ShowName: true } ? Strings.TileMenuHideName : Strings.TileMenuShowName,
            () => _ = session.SetItemsShowNameAsync(
                screen,
                Ids(many),
                picture is not ImageItem { ShowName: true },
                CancellationToken.None)));

        // The same rename as from the stock (Part 7): it changes the name everywhere - the way
        // through the tile is only the shorter one when the picture is in front of the DM anyway.
        if (picture is ImageItem named)
        {
            menu.Items.Add(Entry(Strings.TileMenuRename, () => Renaming?.Invoke(this, named.AssetId)));
        }

        menu.Items.Add(new Separator());

        // <b>Auswählen als Griff, nicht als Fleißarbeit.</b> Die drei Antworten sind die drei
        // Mengen, die es auf einem Screen gibt - alles, was offen liegt, und was weggelegt ist -,
        // und sie stehen hier, weil das, was danach kommt, sie braucht: kopieren und verschieben
        // arbeiten auf der ganzen Auswahl (Handlauf M4, dritter Lauf).
        var choosing = new MenuItem { Header = Strings.TileMenuSelect };

        choosing.Items.Add(Entry(
            Strings.TileMenuSelectAll,
            () => selection.Set(scene.Items.Select(item => item.ItemId))));
        choosing.Items.Add(Entry(
            Strings.TileMenuSelectOnTable,
            () => selection.Set(scene.Items.Where(item => !item.Parked).Select(item => item.ItemId))));
        choosing.Items.Add(Entry(
            Strings.TileMenuSelectInFan,
            () => selection.Set(scene.Items.Where(item => item.Parked).Select(item => item.ItemId))));

        menu.Items.Add(choosing);

        menu.Items.Add(Onto(Strings.TileMenuCopyTo, target =>
            session.CopyItemsAsync(screen, target, Ids(many), CancellationToken.None)));
        menu.Items.Add(Onto(Strings.TileMenuMoveTo, target =>
            session.MoveItemsAsync(screen, target, Ids(many), CancellationToken.None)));

        // The way back from the table into the stock (Part 7) - for the picture pressed on, even when
        // a selection stands: the stock shows one picture at a time this way.
        if (picture is ImageItem shown)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Entry(Strings.TileMenuShowInStock, () => ShowingInStock?.Invoke(this, shown.AssetId)));
        }

        // Set apart, and expressly NOT beside "move to": in the menu they are neighbours, in effect
        // they are opposites, and a slip there clears a picture off the table (Part 7).
        menu.Items.Add(new Separator());
        var removing = Entry(
            Strings.TileMenuRemove,
            () => _ = session.RemoveItemsAsync(screen, Ids(many), CancellationToken.None));

        // The key, written where Windows writes it: right-aligned in the entry, in the text the
        // keyboard shows. InputGestureText only SHOWS it - the key itself is handled by the main
        // window, which is where a key arrives whatever has focus (fourth hand-run, 15.09.2026).
        removing.InputGestureText = Strings.TileMenuRemoveKey;
        menu.Items.Add(removing);

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
            () =>
            {
                _ = session.SetBackgroundFitAsync(screen, fit, CancellationToken.None);

                // Choosing where the layer goes ends moving it by hand. Switching off a mode that
                // is already off does nothing (TileFace.Adjusting).
                Adjusting?.Invoke(this, false);
            });

        entry.IsEnabled = scene.Background is not null;

        // Ticked while the layer still stands in this arrangement. It stays ticked through the
        // hand mode being switched on and off - only an actual movement takes it away
        // (BackgroundItem.Fit).
        entry.IsCheckable = true;
        entry.IsChecked = scene.Background?.Fit == fit;

        return entry;
    }

    /// <summary>
    /// A submenu, and like every other one here a single level of nesting: four equal values laid
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
        var turning = new MenuItem { Header = Strings.TileMenuTurnView };

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
    /// The four quarter turns for a picture or the background, the one the target already stands at
    /// ticked - the same four values and the same tick as <see cref="Turned(ViewRotation)"/>, but
    /// turning what lies on the screen rather than the way the DM looks at it.
    /// </summary>
    private static MenuItem Quarters(string header, double? current, Func<int, Task> turn)
    {
        var entry = new MenuItem { Header = header };
        var standing = current is { } degrees ? CoreManipulation.Quarter(degrees) : null;

        foreach (var angle in (int[])[0, 90, 180, 270])
        {
            var choice = new MenuItem
            {
                Header = $"{angle}°",
                IsCheckable = true,
                IsChecked = angle == standing,
            };

            choice.Click += (_, _) => _ = turn(angle);

            entry.Items.Add(choice);
        }

        return entry;
    }

    /// <summary>
    /// The angle a whole selection stands at, if it stands at one - otherwise nothing is ticked,
    /// because a tick for one of four pictures would describe the other three wrongly.
    /// </summary>
    private static double? Shared(IReadOnlyList<SceneItem> many)
    {
        var quarters = many.Select(item => CoreManipulation.Quarter(item.RotationDeg)).Distinct().ToList();

        return quarters is [{ } quarter] ? quarter : null;
    }

    /// <summary>
    /// One picture set to a quarter turn where it lies. Only the angle is decided here: the hub
    /// clamps the turned picture against the edge, as it does every transform (Part 4, rule 2).
    /// </summary>
    private static ItemTransform Squared(SceneItem item, int angle) =>
        new(item.ItemId, item.CenterX, item.CenterY, item.Scale, angle);

    /// <summary>
    /// A target list over the screens, this one included: copying onto the same screen is what
    /// puts a second guard on the table, and moving onto it does nothing on purpose (Part 4).
    /// </summary>
    private MenuItem Onto(string header, Func<ScreenRef, Task> what)
    {
        var entry = new MenuItem { Header = header };

        foreach (var view in targets())
        {
            var target = view.Screen;
            var choice = new MenuItem { Header = view.Info.Label };

            choice.Click += (_, _) => _ = what(target);

            entry.Items.Add(choice);
        }

        entry.IsEnabled = entry.Items.Count > 0;

        return entry;
    }

    /// <summary>
    /// The same thing the double tap does, and the same arithmetic: the edge nearest the place the
    /// menu was opened on (Part 6). The whole selection turns at once and in one patch - one
    /// command of the DM, one step back out of it.
    /// </summary>
    private Task ToMe(IReadOnlyList<SceneItem> many, ScreenContext context, CorePoint where) =>
        session.TransformItemsAsync(
            screen,
            [.. many.Select(item => Turned(item, context, where))],

            // Turning is not taking hold of: the pictures stay at the depths they had, so this run
            // hands out none and has no order to get wrong.
            toFront: false,
            CancellationToken.None);

    /// <summary>Where one picture ends up when it is turned towards the place the menu was opened on.</summary>
    private static ItemTransform Turned(SceneItem item, ScreenContext context, CorePoint where)
    {
        var turned = CoreManipulation.HoldAtEdge(
            item with { RotationDeg = CoreManipulation.TurnToMe(where, context) },
            context);

        return new ItemTransform(item.ItemId, turned.CenterX, turned.CenterY, turned.Scale, turned.RotationDeg);
    }

    /// <summary>The ids the menu acts on, in the order the selection holds them.</summary>
    private static IReadOnlyList<ItemId> Ids(IReadOnlyList<SceneItem> many) =>
        [.. many.Select(item => item.ItemId)];

    /// <summary>A placement mode as the DM reads it; the enum stays what is stored and compared.</summary>
    private static string Placement(PlacementMode mode) => mode switch
    {
        PlacementMode.Flow => Strings.TileMenuPlacementFlow,
        PlacementMode.Cascade => Strings.TileMenuPlacementCascade,
        _ => mode.ToString(),
    };

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

        // A finger tapping beside the menu did not close it, while a mouse click did (hand-run of
        // M4, 25e): a menu opened from code takes the keyboard focus but not the touch capture, so
        // the touch outside never reaches it. Listening for the next touch anywhere in the window
        // closes it - and the handler takes itself off again, or every menu ever opened would stay
        // subscribed for the life of the process.
        void Beside(object? sender, TouchEventArgs touch) => menu.IsOpen = false;

        if (Window.GetWindow(over) is { } window)
        {
            window.PreviewTouchDown += Beside;
            menu.Closed += (_, _) => window.PreviewTouchDown -= Beside;
        }

        menu.IsOpen = true;
    }
}
