using System.Windows;

namespace DnDOverlay.Control.Tests;

/// <summary>The tab "Layouts" (Part 7).</summary>
public sealed class LayoutsPanelTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// Quitting twice within one minute with "Save layout": the second layout is kept beside the
    /// first, under a numbered name - not lost without a word because the stamp's name was taken
    /// (Part 7: saved under the default name "without asking").
    /// </summary>
    [Fact]
    public async Task Saving_on_quit_twice_in_one_minute_keeps_both_layouts()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));
            await bench.Session.AddItemAsync(CampaignBench.Screen, await bench.PictureAsync("Krieger", 1), position: null, Cancellation);

            var layouts = bench.Parts().Layouts;
            var owner = new Window();

            var first = await layouts.SaveOnQuitAsync(owner);
            var second = await layouts.SaveOnQuitAsync(owner);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotEqual(first, second);
            Assert.Equal(2, bench.Desk.Open!.Shelf.Layouts.Count);
        });
    }
}
