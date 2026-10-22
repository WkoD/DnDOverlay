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
internal sealed class CampaignSwitcher(CampaignDesk desk, Window owner, Action<string> say, Action tidy, string? lastOpen)
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

                label = Strings.CampaignSwitchOpenLabel(label, current.Store.Count, bytes / (1024d * 1024));
            }

            // With nothing open, the campaign open last stands out: a table taken over after a restart
            // most likely comes from it (Part 4).
            var wasOpen = desk.Open is null && string.Equals(lastOpen, summary.Name, StringComparison.OrdinalIgnoreCase);

            if (wasOpen)
            {
                label = Strings.CampaignSwitchLastOpen(label);
            }

            var item = new MenuItem
            {
                Header = label,
                IsChecked = isOpen,
                FontWeight = isOpen || wasOpen ? FontWeights.SemiBold : FontWeights.Normal,
            };

            var name = summary.Name;

            item.Click += async (_, _) => await SwitchAsync(name).ConfigureAwait(true);
            menu.Items.Add(item);
        }

        // An empty list from a folder that cannot be reached is not "no campaigns": they lie on the
        // drive that is gone, and the line says so (Part 3).
        if (campaigns.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = desk.Reachable ? Strings.CampaignSwitchNone : Strings.CampaignsUnreachable(desk.BasePath),
                IsEnabled = false,
            });
        }

        menu.Items.Add(new Separator());

        var setup = new MenuItem { Header = Strings.CampaignSwitchSetUp, IsEnabled = desk.Open is not null };

        setup.Click += (_, _) => new CampaignSetupWindow(owner, desk, say, tidy).Show();
        menu.Items.Add(setup);

        // For the OPEN campaign, not the one tapped in the list: a closed campaign is not looked into
        // (Part 7).
        var tidying = new MenuItem { Header = Strings.CampaignSwitchTidy, IsEnabled = desk.Open is not null };

        tidying.Click += (_, _) => tidy();
        menu.Items.Add(tidying);

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
                Strings.CampaignSwitchQuestion(name, open.Name),
                Strings.CampaignSwitchTitle,
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
