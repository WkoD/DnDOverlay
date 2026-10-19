using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DnDOverlay.Control;

/// <summary>
/// The campaign switcher (Part 7): the names of all campaigns, the open one marked and the only one
/// with its numbers - they are in memory anyway - and below it the way to "Set up campaign".
/// <para>
/// <b>Of other campaigns nothing but the name</b>, so opening the switcher costs one directory
/// listing and not a byte of a foreign inventory (Part 3). <b>A switch asks first</b>: the table is
/// cleared, because its pictures come from the stock that closes, and undo does not reach past it.
/// </para>
/// </summary>
internal sealed class CampaignSwitcher(CampaignDesk desk, Window owner, Action<string> say)
{
    /// <summary>Opens the list under the campaign button.</summary>
    internal void Open(UIElement under)
    {
        var menu = new ContextMenu { PlacementTarget = under, Placement = PlacementMode.Bottom };
        var campaigns = desk.List();

        foreach (var summary in campaigns)
        {
            var isOpen = desk.Open is { } open && string.Equals(open.Name, summary.Name, StringComparison.OrdinalIgnoreCase);
            var label = CampaignDesk.Label(summary);

            if (isOpen && desk.Open is { } current)
            {
                var bytes = current.Store.Entries.Sum(entry => entry.Bytes);

                label += string.Create(CultureInfo.CurrentCulture, $"  - {current.Store.Count} picture(s), {bytes / (1024d * 1024):0.#} MB");
            }

            var item = new MenuItem
            {
                Header = label,
                IsChecked = isOpen,
                FontWeight = isOpen ? FontWeights.SemiBold : FontWeights.Normal,
            };

            var name = summary.Name;

            item.Click += async (_, _) => await SwitchAsync(name).ConfigureAwait(true);
            menu.Items.Add(item);
        }

        if (campaigns.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No campaign yet - add pictures to begin one", IsEnabled = false });
        }

        menu.Items.Add(new Separator());

        var setup = new MenuItem { Header = "Set up campaign ...", IsEnabled = desk.Open is not null };

        setup.Click += (_, _) => new CampaignSetupWindow(owner, desk).Show();
        menu.Items.Add(setup);

        menu.IsOpen = true;
    }

    private async Task SwitchAsync(string name)
    {
        if (desk.Open is { } open)
        {
            if (string.Equals(open.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Says what happens rather than only "really?", or it would look arbitrary beside all the
            // grips that ask nothing (Part 7).
            var answer = MessageBox.Show(
                owner,
                $"Switch to \"{name}\"?\n\nThe table is cleared on every screen - its pictures come from \"{open.Name}\" - and undo cannot go back past this.",
                "Switch campaign",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (await desk.SwitchAsync(name).ConfigureAwait(true) is { } refused)
        {
            say(refused);
        }
    }
}
