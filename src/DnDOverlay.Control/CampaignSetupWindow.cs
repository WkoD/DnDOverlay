using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Campaign;

namespace DnDOverlay.Control;

/// <summary>
/// "Set up campaign" (Part 7): what one does once per campaign - rename it with its folder, open the
/// folder, see how much is in it and the way to the tidy-up view beside the number, delete it. An
/// ordinary window, not modal.
/// </summary>
internal sealed class CampaignSetupWindow : Window
{
    private readonly CampaignDesk _desk;
    private readonly Action<string> _say;
    private readonly TextBox _name = new() { Padding = new Thickness(4), MinWidth = 260 };
    private readonly TextBlock _facts = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _said = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>The campaign whose name stands in the box - written again only when it is another one.</summary>
    private OpenCampaign? _named;

    internal CampaignSetupWindow(Window owner, CampaignDesk desk, Action<string> say, Action tidy)
    {
        Owner = owner;
        _desk = desk;
        _say = say;

        Title = Strings.CampaignSetupTitle;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var apply = new Button { Content = Strings.CampaignSetupApply, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        var folder = new Button { Content = Strings.CampaignSetupOpenFolder, Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        var tidying = new Button { Content = Strings.CampaignSwitchTidy, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 10, 0, 0) };
        var delete = new Button { Content = Strings.CampaignSetupDelete, Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };

        var naming = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

        naming.Children.Add(_name);
        naming.Children.Add(apply);

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock { Text = Strings.CampaignSetupName, FontWeight = FontWeights.Bold });
        panel.Children.Add(naming);
        panel.Children.Add(_said);
        panel.Children.Add(_facts);

        // Number and action side by side (Part 7): beside the count and the room, the way to the view
        // that shows which pictures take it.
        var actions = new StackPanel { Orientation = Orientation.Horizontal };

        actions.Children.Add(folder);
        actions.Children.Add(tidying);
        panel.Children.Add(actions);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 10) });
        panel.Children.Add(delete);

        Content = panel;

        // Set with "Apply", not on every keystroke: it renames a folder, it is not a switch (Part 7).
        apply.Click += (_, _) => Rename();
        folder.Click += (_, _) =>
        {
            if (_desk.Open is { } open)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{open.Directory}\"") { UseShellExecute = true });
            }
        };
        tidying.Click += (_, _) => tidy();
        delete.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);

        // A picture taken in, renamed or deleted changes the count and the room as much as a switch.
        _desk.Changed += Refresh;
        _desk.StockChanged += Refresh;
        Closed += (_, _) =>
        {
            _desk.Changed -= Refresh;
            _desk.StockChanged -= Refresh;
        };

        Refresh(this, EventArgs.Empty);
    }

    private void Refresh(object? sender, EventArgs e)
    {
        if (_desk.Open is not { } open)
        {
            Close();
            return;
        }

        // Only for another campaign - a rename is one - so what the DM is typing survives the
        // adoption after a folder check, which raises the same change.
        if (!ReferenceEquals(open, _named))
        {
            _named = open;
            _name.Text = open.Unnamed ? string.Empty : open.Name;
        }

        var bytes = open.Store.Entries.Sum(entry => entry.Bytes);

        _facts.Text = Strings.CampaignSetupFacts(open.Store.Count, bytes / (1024d * 1024), open.Shelf.Scenes.Count, open.Shelf.Layouts.Count, open.Directory);
    }

    private void Rename()
    {
        if (_desk.Refusal is { } refused)
        {
            _said.Text = refused;
            return;
        }

        if (FileNames.Check(_name.Text) is { } problem)
        {
            _said.Text = NameDialog.Explain(problem);
            return;
        }

        _said.Text = _desk.Rename(_name.Text) switch
        {
            CampaignOutcome.Done => Strings.CampaignSetupRenamed,
            CampaignOutcome.Taken => Strings.CampaignSetupNameTaken(_name.Text),
            CampaignOutcome.Busy => Strings.CampaignSetupBusy,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The one grip that cannot be taken back, and the heaviest question in the program (Part 7): it
    /// names what goes, points at the folder while it still exists, and wants the name TYPED - a
    /// slip costs months of work, and the grip comes twice in the life of an installation.
    /// </summary>
    private async Task DeleteAsync()
    {
        if (_desk.Open is not { } open)
        {
            return;
        }

        var bytes = open.Store.Entries.Sum(entry => entry.Bytes);

        var typed = NameDialog.Ask(
            this,
            Strings.CampaignSetupDeleteTitle,
            Strings.CampaignSetupDeletePrompt(open.Name, open.Store.Count, open.Shelf.Scenes.Count, open.Shelf.Layouts.Count, bytes / (1024d * 1024), open.Directory),
            string.Empty,
            check: false);

        if (typed is null)
        {
            return;
        }

        if (!string.Equals(typed, open.Name, StringComparison.Ordinal))
        {
            _said.Text = Strings.CampaignSetupNoMatch;
            return;
        }

        // Said in the window while it is still open, and in the status line: a delete that went
        // through by half has closed the campaign, and with it this window.
        if (await _desk.DeleteAsync().ConfigureAwait(true) is { } refused)
        {
            _said.Text = refused;
            _say(refused);
        }
    }
}
