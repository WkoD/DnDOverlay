using System.IO;
using DnDOverlay.Campaign;
using DnDOverlay.Core;

namespace DnDOverlay.Control.Tests;

/// <summary>The one path every entrance ends on (Part 7).</summary>
public sealed class IntakeRunnerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// A picture taken in a second time gets no second entry - it gets shown: the run names the entry
    /// that was there, under the name it has (Part 7), for the stock to narrow to it.
    /// </summary>
    [Fact]
    public async Task A_picture_already_in_the_stock_is_handed_on_to_be_shown()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var known = await bench.PictureAsync("Grimmbart", 3);
            var file = Path.Combine(bench.Base, "ork_04.png");

            await File.WriteAllBytesAsync(file, CampaignBench.Png(3), Cancellation);

            var intake = bench.Parts().Intake;
            IReadOnlyList<AssetRef>? shown = null;

            intake.AlreadyThere += (_, assets) => shown = assets;
            await intake.RunAsync(intake.Entrances.FromFiles([file]), target: null);

            var asset = Assert.Single(shown!);

            Assert.Equal(known.AssetId, asset.AssetId);
            Assert.Equal("Grimmbart", asset.Name);
        });
    }

    /// <summary>An entrance that offered nothing usable says so, and no run starts.</summary>
    [Fact]
    public async Task Nothing_usable_is_said_and_starts_no_run()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var intake = bench.Parts().Intake;
            var runs = 0;

            intake.RunningChanged += (_, _) => runs++;
            await intake.RunAsync([], target: null);

            Assert.Equal(Strings.IntakeNothingUsable, Assert.Single(bench.Said));
            Assert.Equal(0, runs);
        });
    }

    /// <summary>
    /// One run at a time: a second entrance while pictures come in is refused and said - two runs
    /// would report into one status line and break off each other (Part 7).
    /// </summary>
    [Fact]
    public async Task A_second_run_while_one_is_going_is_refused()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var intake = bench.Parts().Intake;
            var held = new TaskCompletionSource<IntakeBytes>(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = new List<bool>();

            intake.RunningChanged += (_, going) => running.Add(going);

            var first = intake.RunAsync([new IntakeSource("Karte", _ => new ValueTask<IntakeBytes>(held.Task))], target: null);

            Assert.True(intake.Running);

            await intake.RunAsync([new IntakeSource("Grimmbart", _ => ValueTask.FromResult<IntakeBytes>(new IntakeBytes.Ready(CampaignBench.Png(2))))], target: null);

            Assert.Contains(Strings.IntakeRunGoing, bench.Said);

            held.SetResult(new IntakeBytes.Ready(CampaignBench.Png(1)));
            await first;

            Assert.False(intake.Running);
            Assert.Equal([true, false], running);
            Assert.Equal(Strings.IntakeTaken(1) + ".", bench.Said.Last());
        });
    }

    /// <summary>
    /// A background layer holds one picture: a run of several aimed at it uses the first and says what
    /// it did not use - the rest stays in the stock (Part 7).
    /// </summary>
    [Fact]
    public async Task Several_pictures_aimed_at_the_background_use_the_first_and_say_the_rest()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var intake = bench.Parts().Intake;

            await intake.RunAsync([Ready("Karte", 1), Ready("Grimmbart", 2), Ready("Ork", 3)], CampaignBench.Screen, background: true);

            var scene = await bench.Session.GetSceneAsync(CampaignBench.Screen, Cancellation);

            Assert.Equal("Karte", scene.Background?.Name);
            Assert.Empty(scene.Items);
            Assert.EndsWith(Strings.IntakeBackgroundRest("Karte", 2), bench.Said.Last(), StringComparison.Ordinal);
            Assert.Equal(3, bench.Stock.Current!.Count);
        });
    }

    /// <summary>One picture let go at a place lands there; a run flows from the screen's placement (Part 7).</summary>
    [Fact]
    public async Task One_picture_let_go_on_a_tile_lands_where_it_was_let_go()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var intake = bench.Parts().Intake;

            await intake.RunAsync([Ready("Karte", 1)], CampaignBench.Screen, at: new Point(0.25, 0.75));

            var item = Assert.Single((await bench.Session.GetSceneAsync(CampaignBench.Screen, Cancellation)).Items);

            Assert.Equal(0.25, item.CenterX, 6);
            Assert.Equal(0.75, item.CenterY, 6);
        });
    }

    /// <summary>A refused source is named with its reason in the ONE collected message - not a dialog per file (Part 7).</summary>
    [Fact]
    public async Task A_refused_picture_is_named_in_the_one_message()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var intake = bench.Parts().Intake;
            var gone = new IntakeSource("Weg", _ => ValueTask.FromResult<IntakeBytes>(new IntakeBytes.Unavailable(IntakeRejection.Unreachable, "nicht erreichbar")));

            await intake.RunAsync([Ready("Karte", 1), gone], target: null);

            var said = bench.Said.Last();

            Assert.StartsWith(Strings.IntakeTaken(1), said, StringComparison.Ordinal);
            Assert.Contains("Weg: nicht erreichbar", said, StringComparison.Ordinal);
        });
    }

    private static IntakeSource Ready(string name, int seed) =>
        new(name, _ => ValueTask.FromResult<IntakeBytes>(new IntakeBytes.Ready(CampaignBench.Png(seed))));

    /// <summary>
    /// A copied page whose fragment links to a file on this machine: the link names what the page
    /// points to, not the copied picture. The offer's own bitmap comes in - not the linked file, which
    /// files-first would otherwise put ahead of it.
    /// </summary>
    [Fact]
    public async Task A_link_on_a_copied_page_does_not_win_over_its_picture()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);
            var linked = Path.Combine(bench.Base, "notizen.png");
            var copied = CampaignBench.Png(2);

            await File.WriteAllBytesAsync(linked, CampaignBench.Png(1), Cancellation);

            var data = new System.Windows.DataObject();

            data.SetData(System.Windows.DataFormats.Html, $"<a href=\"{new Uri(linked).AbsoluteUri}\">Notizen</a>");
            data.SetData("PNG", new MemoryStream(copied));

            var source = Assert.Single(bench.Parts().Intake.Entrances.FromDataObject(data));
            var bytes = Assert.IsType<IntakeBytes.Ready>(await source.Open(Cancellation));

            Assert.Equal(copied, bytes.Data.ToArray());
        });
    }
}
