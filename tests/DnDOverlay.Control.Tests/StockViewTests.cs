using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DnDOverlay.Campaign;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control.Tests;

/// <summary>The tab "Pictures" (Part 7): the grid, its marks, and the grips on its entries.</summary>
public sealed class StockViewTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// A picture laid on a screen - a patch, as twenty a second while a token is pushed at the table:
    /// the mark "on 1" follows, and the tile that stands is the same element. A grid built anew would
    /// take the card a mouse carry holds its capture on out of the tree and end the drag.
    /// </summary>
    [Fact]
    public async Task The_marks_follow_the_table_without_drawing_the_grid_again()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var picture = await bench.PictureAsync("Krieger", 1);
            var view = bench.Parts().Stock;

            view.Apply(string.Empty, PanelSort.Newest, 128, PanelSide.Right);

            var card = Assert.Single(Cards(view));

            await bench.Session.AddItemAsync(CampaignBench.Screen, picture, position: null, Cancellation);
            await view.CountAsync();

            Assert.Same(card, Assert.Single(Cards(view)));
            Assert.Contains(Texts(card), text => text.Contains(Strings.StockMarkOn(1), StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// Two pictures chosen, one of them double-clicked: both go to the active screen (Part 7: "double
    /// tap sends the selection when the entry hit belongs to it"). The release between the two presses
    /// narrows the selection to the one under the hand - the double click must not find it narrowed.
    /// </summary>
    [Fact]
    public async Task A_double_click_on_a_chosen_picture_sends_the_whole_selection()
    {
        using var thread = new DispatcherThread();

        await thread.On(async () =>
        {
            using var bench = new CampaignBench(thread.Dispatcher);

            await bench.Desk.SwitchAsync(bench.Campaign("Vandalia"));

            var orc = await bench.PictureAsync("Ork", 1);
            var goblin = await bench.PictureAsync("Goblin", 2);
            var view = bench.Parts().Stock;

            view.Apply(string.Empty, PanelSort.Newest, 128, PanelSide.Right);
            view.Reveal([orc.AssetId, goblin.AssetId]);

            var card = Cards(view).Single(found => ((InventoryEntry)found.Tag).AssetId == orc.AssetId.Value);

            Press(card, UIElement.MouseLeftButtonDownEvent, clicks: 1);
            Press(card, UIElement.MouseLeftButtonUpEvent, clicks: 1);
            Press(card, UIElement.MouseLeftButtonDownEvent, clicks: 2);

            var scene = await SettledAsync(bench, expected: 2);

            Assert.Equal(2, scene.Items.Count);
        });
    }

    /// <summary>The cards of the grid, in the order they stand.</summary>
    internal static List<Border> Cards(StockView view) =>
        [.. ((WrapPanel)((Grid)((ScrollViewer)view.Children[0]).Content).Children[0]).Children.OfType<Border>()];

    /// <summary>Every text written on an element and inside it.</summary>
    private static List<string> Texts(DependencyObject element)
    {
        var texts = new List<string>();

        if (element is TextBlock block)
        {
            texts.Add(block.Text);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            texts.AddRange(Texts(child));
        }

        return texts;
    }

    /// <summary>A mouse button as WPF reports it, with the click count it counted.</summary>
    private static void Press(UIElement card, RoutedEvent routed, int clicks)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed };

        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, clicks);
        card.RaiseEvent(args);
    }

    /// <summary>The screen once the sending the grip started has arrived - or as it stands after a while.</summary>
    private static async Task<SceneState> SettledAsync(CampaignBench bench, int expected)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var scene = await bench.Session.GetSceneAsync(CampaignBench.Screen, Cancellation);

            if (scene.Items.Count >= expected)
            {
                return scene;
            }

            await Task.Delay(20, Cancellation);
        }

        return await bench.Session.GetSceneAsync(CampaignBench.Screen, Cancellation);
    }
}
