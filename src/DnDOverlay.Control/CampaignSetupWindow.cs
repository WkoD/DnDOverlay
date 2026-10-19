using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Campaign;

namespace DnDOverlay.Control;

/// <summary>
/// "Set up campaign" (Part 7): what one does once per campaign - rename it with its folder, open the
/// folder, see how much is in it, delete it. An ordinary window, not modal.
/// </summary>
internal sealed class CampaignSetupWindow : Window
{
    private readonly CampaignDesk _desk;
    private readonly TextBox _name = new() { Padding = new Thickness(4), MinWidth = 260 };
    private readonly TextBlock _facts = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _said = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    internal CampaignSetupWindow(Window owner, CampaignDesk desk)
    {
        Owner = owner;
        _desk = desk;

        Title = "Set up campaign";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var apply = new Button { Content = "Apply", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        var folder = new Button { Content = "Open folder in Explorer", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        var delete = new Button { Content = "Delete campaign ...", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };

        var naming = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

        naming.Children.Add(_name);
        naming.Children.Add(apply);

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock { Text = "Name", FontWeight = FontWeights.Bold });
        panel.Children.Add(naming);
        panel.Children.Add(_said);
        panel.Children.Add(_facts);
        panel.Children.Add(folder);
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
        delete.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);

        _desk.Changed += Refresh;
        Closed += (_, _) => _desk.Changed -= Refresh;

        Refresh(this, EventArgs.Empty);
    }

    private void Refresh(object? sender, EventArgs e)
    {
        if (_desk.Open is not { } open)
        {
            Close();
            return;
        }

        _name.Text = open.Unnamed ? string.Empty : open.Name;

        var bytes = open.Store.Entries.Sum(entry => entry.Bytes);

        _facts.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{open.Store.Count} picture(s), {bytes / (1024d * 1024):0.#} MB, {open.Shelf.Scenes.Count} scene(s), {open.Shelf.Layouts.Count} layout(s).\n{open.Directory}");
    }

    private void Rename()
    {
        if (FileNames.Check(_name.Text) is { } problem)
        {
            _said.Text = NameDialog.Explain(problem);
            return;
        }

        _said.Text = _desk.Rename(_name.Text) switch
        {
            CampaignOutcome.Done => "Renamed.",
            CampaignOutcome.Taken => $"A campaign named \"{_name.Text}\" exists already.",
            CampaignOutcome.Busy => "The folder is in use right now - try again in a moment.",
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
        var what = string.Create(
            CultureInfo.CurrentCulture,
            $"\"{open.Name}\" - {open.Store.Count} picture(s), {open.Shelf.Scenes.Count} scene(s), {open.Shelf.Layouts.Count} layout(s), {bytes / (1024d * 1024):0.#} MB.");

        var typed = NameDialog.Ask(
            this,
            "Delete campaign",
            $"This deletes {what}\nUndo cannot bring it back. To keep a copy, copy the folder first:\n{open.Directory}\n\nType the campaign's name to delete it:",
            string.Empty,
            check: false);

        if (typed is null)
        {
            return;
        }

        if (!string.Equals(typed, open.Name, StringComparison.Ordinal))
        {
            _said.Text = "The name did not match - nothing was deleted.";
            return;
        }

        await _desk.DeleteAsync().ConfigureAwait(true);
    }
}
