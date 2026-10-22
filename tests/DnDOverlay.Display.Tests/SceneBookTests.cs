using DnDOverlay.Core;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// The scenes are written from the connection task and the UI thread at once: neither may lose the
/// other's change (Part 4, conflict rule 2).
/// </summary>
public sealed class SceneBookTests
{
    private static readonly DeviceId Device = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly ScreenRef Table = new(Device, new ScreenId("table"));

    [Fact]
    public async Task Two_threads_changing_one_scene_lose_nothing()
    {
        const int Each = 20_000;

        var book = new SceneBook();
        book.Set(Table, SceneWith(Card(zOrder: 0)));

        using var start = new Barrier(2);

        void Raise()
        {
            start.SignalAndWait();

            for (var i = 0; i < Each; i++)
            {
                // A read-modify-write, the shape of both a patch and a gesture.
                book.Change(Table, scene => SceneWith(scene!.Items[0] with { ZOrder = scene.Items[0].ZOrder + 1 }));
            }
        }

        await Task.WhenAll(Task.Run(Raise, TestContext.Current.CancellationToken), Task.Run(Raise, TestContext.Current.CancellationToken));

        Assert.True(book.TryGetValue(Table, out var final));
        Assert.Equal(2 * Each, final.Items[0].ZOrder);
    }

    /// <summary>A gesture for a screen without a scene must not invent one - it would count as "this screen has its scene".</summary>
    [Fact]
    public void A_change_that_answers_nothing_writes_nothing()
    {
        var book = new SceneBook();

        book.Change(Table, _ => null);

        Assert.False(book.ContainsKey(Table));
    }

    [Fact]
    public void A_fresh_identity_takes_its_scenes_along_and_leaves_others()
    {
        var fresh = new DeviceId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var stranger = new ScreenRef(new DeviceId(Guid.Parse("33333333-3333-3333-3333-333333333333")), new ScreenId("table"));
        var mine = SceneWith(Card(zOrder: 4));
        var theirs = SceneWith(Card(zOrder: 9));

        var book = new SceneBook();
        book.Set(Table, mine);
        book.Set(stranger, theirs);

        book.Rekey(Device, fresh);

        Assert.False(book.ContainsKey(Table));
        Assert.True(book.TryGetValue(Table with { Device = fresh }, out var moved));
        Assert.Same(mine, moved);
        Assert.True(book.TryGetValue(stranger, out var kept));
        Assert.Same(theirs, kept);
    }

    private static TextItem Card(int zOrder) =>
        new(new ItemId(Guid.Parse("44444444-4444-4444-4444-444444444444")), 0.5, 0.5, 0.1, 1, 0, zOrder, false, false, 1, "Dilwyn");

    private static SceneState SceneWith(SceneItem item) => SceneState.Empty with { Items = [item] };
}
