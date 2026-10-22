using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Core;
using DnDOverlay.Hub;

namespace DnDOverlay.Control;

/// <summary>
/// Which known device a waiting one really is (Part 7, "assign to a known device") - and what goes
/// wrong if the answer is wrong, said plainly rather than as a warning.
/// </summary>
internal sealed class AdoptQuestion : Window
{
    private readonly ListBox _known = new() { MinHeight = 80, Margin = new Thickness(0, 0, 0, 10) };

    private AdoptQuestion(Window owner, PendingPairing request, IReadOnlyList<DeviceView> candidates)
    {
        Owner = owner;
        Title = Strings.AdoptTitle;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var device in candidates)
        {
            _known.Items.Add(new ListBoxItem { Content = Strings.AdoptCandidate(device.Name, device.Screens.Count), Tag = device });
        }

        _known.SelectedIndex = 0;

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(Text(Strings.AdoptIntro(request.Name, request.Address, request.PairingCode)));
        panel.Children.Add(_known);
        panel.Children.Add(Text(Strings.AdoptTakesOver));
        panel.Children.Add(Text(Strings.AdoptRisk));
        panel.Children.Add(Text(Strings.AdoptNoRestore));

        var assign = new Button { Content = Strings.AdoptAssign, IsDefault = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Strings.AdoptCancel, IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };

        assign.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(assign);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
    }

    internal static DeviceView? Ask(Window owner, PendingPairing request, IReadOnlyList<DeviceView> candidates)
    {
        var question = new AdoptQuestion(owner, request, candidates);

        return question.ShowDialog() == true && question._known.SelectedItem is ListBoxItem { Tag: DeviceView device } ? device : null;
    }

    private static TextBlock Text(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
}
