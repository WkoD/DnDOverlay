namespace DnDOverlay.Core.Tests;

/// <summary>Undo and redo (Part 4, Part 11): a step is what one act added and removed, and nothing else.</summary>
public sealed class TimelineTests
{
    private static readonly DeviceId Device = new(Guid.Parse("dddddddd-0000-0000-0000-000000000001"));
    private static readonly ScreenRef Table = new(Device, new ScreenId(@"\\?\DISPLAY#TABLE"));
    private static readonly ScreenRef Wall = new(Device, new ScreenId(@"\\?\DISPLAY#WALL"));
    private static readonly ScreenContext Context = ScreenContext.Default(new PixelSize(1920, 1080), 96);

    /// <summary>
    /// The most important test (Part 11): picture A laid, five others pushed, turned and zoomed since -
    /// from the table or the thumbnail alike - and A taken back. A goes, the five stay exactly where
    /// they were last put.
    /// </summary>
    [Fact]
    public void The_counter_move_is_surgical()
    {
        var five = Enumerable.Range(0, 5).Select(index => Item(0.1 + (index * 0.1))).ToList();
        var a = Item(0.9);
        var before = Scene([.. five]);
        var step = Step("Add A", (Table, before), (Table, Scene([.. five, a])));

        var moved = five.Select(item => (SceneItem)(item with { CenterX = 0.5, CenterY = 0.8, RotationDeg = 33, Scale = 0.2 })).ToList();
        var table = Apply(Scene([.. moved, a]), step.UndoOps());

        Assert.DoesNotContain(table.Items, item => item.ItemId == a.ItemId);
        Assert.Equal(moved, table.Items);
    }

    /// <summary>Undoing a clear brings everything back - locked, parked, depth and the background - as one step.</summary>
    [Fact]
    public void Undoing_a_clear_brings_back_items_and_background()
    {
        var locked = Item(0.2) with { Locked = true, ZOrder = 3 };
        var parked = Item(0.4) with { Parked = true, ZOrder = 1 };
        var full = Scene(locked, parked) with { Background = Background() };
        var step = Step("Clear", (Table, full), (Table, SceneState.Empty));

        var restored = Apply(SceneState.Empty, step.UndoOps());

        Assert.Contains(locked, restored.Items);
        Assert.Contains(restored.Items, item => item.ItemId == parked.ItemId && item.Parked && item.ZOrder == 1);
        Assert.Equal(full.Background, restored.Background);
    }

    /// <summary>A move taken back puts the picture on its source only, with the same id; done again, it is on the target.</summary>
    [Fact]
    public void A_move_taken_back_is_on_its_source_only()
    {
        var picture = Item(0.3);
        var step = Step(
            "Move",
            [(Table, Scene(picture)), (Wall, SceneState.Empty)],
            [(Table, SceneState.Empty), (Wall, Scene(picture with { CenterX = 0.7 }))]);

        var undo = step.UndoOps();

        Assert.Contains(undo, op => op.Screen == Wall && op.Op is RemoveItem remove && remove.Item == picture.ItemId);
        Assert.Contains(undo, op => op.Screen == Table && op.Op is AddItem add && add.Item == picture);

        var redo = step.RedoOps();

        Assert.Contains(redo, op => op.Screen == Table && op.Op is RemoveItem);
        Assert.Contains(redo, op => op.Screen == Wall && op.Op is AddItem add && add.Item.CenterX == 0.7);
    }

    [Fact]
    public void A_command_that_changed_nothing_is_no_step()
    {
        var scene = Scene(Item(0.5));

        Assert.Null(TimelineStep.Between("Nothing", Map((Table, scene)), Map((Table, scene with { Items = [scene.Items[0] with { CenterX = 0.6 }] }))));
    }

    /// <summary>Fifty acts, fifty times back - the start; once more does nothing and is no error. The next act drops redo.</summary>
    [Fact]
    public void Fifty_back_reach_the_start_and_a_new_act_drops_redo()
    {
        var timeline = new Timeline();

        for (var index = 0; index < 50; index++)
        {
            timeline.Record(Added($"Step {index}"));
        }

        for (var index = 0; index < 50; index++)
        {
            Assert.NotNull(timeline.Undo());
        }

        Assert.Null(timeline.Undo());
        Assert.Equal("Step 0", timeline.RedoLabel);

        timeline.Record(Added("Fresh"));

        Assert.Null(timeline.RedoLabel);
        Assert.Null(timeline.Redo());
    }

    /// <summary>The label names the next grip (Part 11): after A and B, "B"; after one undo, undo says A and redo says B.</summary>
    [Fact]
    public void The_label_names_the_next_grip()
    {
        var timeline = new Timeline();

        timeline.Record(Added("A"));
        timeline.Record(Added("B"));

        Assert.Equal("B", timeline.UndoLabel);

        timeline.Undo();

        Assert.Equal("A", timeline.UndoLabel);
        Assert.Equal("B", timeline.RedoLabel);
    }

    [Fact]
    public void Past_the_limit_the_oldest_step_goes()
    {
        var timeline = new Timeline();

        for (var index = 0; index <= Timeline.Limit; index++)
        {
            timeline.Record(Added($"Step {index}"));
        }

        Assert.Equal(Timeline.Limit, timeline.Depth);

        while (timeline.Undo() is not null)
        {
        }

        Assert.Equal("Step 1", timeline.RedoLabel);
    }

    /// <summary>
    /// Deleting a picture from the stock cuts undo up to the newest step that shows it - nothing older
    /// can be reached past it - and drops redo when redo could bring it back. The count says what went.
    /// </summary>
    [Fact]
    public void A_delete_cuts_up_to_the_newest_step_showing_the_picture()
    {
        var orc = Item(0.2, asset: 'a');
        var goblin = Item(0.4, asset: 'b');
        var timeline = new Timeline();

        timeline.Record(Step("Add orc", (Table, SceneState.Empty), (Table, Scene(orc))));
        timeline.Record(Step("Add goblin", (Table, Scene(orc)), (Table, Scene(orc, goblin))));
        timeline.Record(Step("Remove orc", (Table, Scene(orc, goblin)), (Table, Scene(goblin))));
        timeline.Record(Added("Later"));
        timeline.Record(Added("Undone"));
        timeline.Undo();

        var gone = new HashSet<AssetId> { orc.AssetId };

        Assert.Equal(3, timeline.CountLost(gone));
        Assert.Equal(3, timeline.Truncate(gone));
        Assert.Equal(1, timeline.Depth);
        Assert.Equal("Later", timeline.UndoLabel);
        Assert.Equal("Undone", timeline.RedoLabel);
    }

    /// <summary>
    /// Tokens laid on a map keep the map as their background before and after, unchanged - deleting
    /// the map must not cut them: none of their ops could bring it back (Part 7).
    /// </summary>
    [Fact]
    public void A_background_the_step_did_not_change_does_not_tie_it_to_the_map()
    {
        var map = SceneState.Empty with { Background = Background() };
        var timeline = new Timeline();

        timeline.Record(Step("Set map", (Table, SceneState.Empty), (Table, map)));
        timeline.Record(Step("Token 1", (Table, map), (Table, map with { Items = [Item(0.2)] })));
        timeline.Record(Step("Token 2", (Table, map), (Table, map with { Items = [Item(0.4)] })));

        var gone = new HashSet<AssetId> { map.Background!.AssetId };

        Assert.Equal(1, timeline.CountLost(gone));
        Assert.Equal(1, timeline.Truncate(gone));
        Assert.Equal(2, timeline.Depth);
    }

    /// <summary>
    /// A rename in the stock reaches the steps (Part 3, "one picture, one name"): undo and redo lay
    /// the picture down under its new name - items and background alike - not under the recorded one.
    /// </summary>
    [Fact]
    public void A_rename_reaches_what_undo_and_redo_bring_back()
    {
        var krieger = Item(0.3) with { Name = "Krieger" };
        var other = Item(0.6, asset: 'b') with { Name = "Ork" };
        var map = Background();
        var timeline = new Timeline();

        timeline.Record(Step("Remove", (Table, Scene(krieger, other) with { Background = map }), (Table, SceneState.Empty)));
        timeline.Record(Step("Add", (Table, SceneState.Empty), (Table, Scene(krieger))));
        timeline.Undo();

        timeline.Rename(krieger.AssetId, "Grimmbart");
        timeline.Rename(map.AssetId, "Weltkarte");

        var redo = timeline.Redo()!.RedoOps();
        var undo = timeline.Undo()!.UndoOps();
        var older = timeline.Undo()!.UndoOps();

        Assert.Contains(redo, op => op.Op is AddItem { Item: ImageItem { Name: "Grimmbart" } });
        Assert.Contains(older, op => op.Op is AddItem { Item: ImageItem { Name: "Grimmbart" } });
        Assert.Contains(older, op => op.Op is AddItem { Item: ImageItem { Name: "Ork" } });
        Assert.Contains(older, op => op.Op is SetBackground { Background.Name: "Weltkarte" });
        Assert.DoesNotContain(undo.Concat(redo).Concat(older), op => op.Op is AddItem { Item: ImageItem { Name: "Krieger" } });

        // Renamed with two steps waiting for redo: they come back in the order they were undone.
        timeline.Rename(other.AssetId, "Orkhäuptling");

        Assert.Equal("Remove", timeline.Redo()!.Label);
        Assert.Equal("Add", timeline.Redo()!.Label);
    }

    /// <summary>
    /// The recording session writes from the thread pool while the buttons and the delete question
    /// read on the UI thread: reading while another thread records never fails.
    /// </summary>
    [Fact]
    public async Task Reading_while_another_thread_records_never_fails()
    {
        var timeline = new Timeline();
        var gone = new HashSet<AssetId> { new(new string('z', 64)) };
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var writer = Task.Run(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    timeline.Record(Added("Step"));
                    timeline.Undo();
                    timeline.Redo();
                }
            },
            TestContext.Current.CancellationToken);

        while (!writer.IsCompleted)
        {
            _ = timeline.CountLost(gone);
            _ = timeline.RedoLabel;
            _ = timeline.UndoLabel;
        }

        await writer;
    }

    private static TimelineStep Added(string label) =>
        Step(label, (Table, SceneState.Empty), (Table, Scene(Item(0.5))));

    private static TimelineStep Step(string label, (ScreenRef, SceneState) before, (ScreenRef, SceneState) after) =>
        Step(label, [before], [after]);

    private static TimelineStep Step(string label, (ScreenRef, SceneState)[] before, (ScreenRef, SceneState)[] after) =>
        TimelineStep.Between(label, Map(before), Map(after)) ?? throw new InvalidOperationException("no step");

    private static Dictionary<ScreenRef, SceneState> Map(params (ScreenRef Screen, SceneState Scene)[] scenes) =>
        scenes.ToDictionary(pair => pair.Screen, pair => pair.Scene);

    private static SceneState Apply(SceneState scene, IReadOnlyList<ScreenOp> ops) =>
        ops.Where(op => op.Screen == Table).Aggregate(scene, (current, op) => SceneReducer.Apply(current, op.Op, Context));

    private static SceneState Scene(params SceneItem[] items) => SceneState.Empty with { Items = items };

    private static ImageItem Item(double x, char asset = 'c') =>
        new(
            new ItemId(Guid.NewGuid()), x, 0.5, 0.3, 4d / 3d, 0, 1, false, false, 1,
            new AssetId(new string(asset, 64)),
            new AssetMeta(800, 600, "png", 1024, false, new string('d', 64)),
            "Bild",
            false,
            false);

    private static BackgroundItem Background() =>
        new(new AssetId(new string('e', 64)), new AssetMeta(1920, 1080, "png", 2048, false, new string('f', 64)), "Karte", false, 0.5, 0.5, 1, 0, false);
}
