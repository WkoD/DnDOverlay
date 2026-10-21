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
        Title = "Assign to a known device";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var device in candidates)
        {
            _known.Items.Add(new ListBoxItem { Content = $"{device.Name} - {device.Screens.Count} screen(s), not connected", Tag = device });
        }

        _known.SelectedIndex = 0;

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(Text($"{request.Name} at {request.Address}, code {request.PairingCode}, is to take over the entry of:"));
        panel.Children.Add(_known);
        panel.Children.Add(Text("It takes over the name, the remote settings, the screens and what lies on them; the old entry disappears. Screens on the same hardware carry straight on; after new hardware they stand beside the old ones and are mapped with \"Take over\" in the screen's band."));
        panel.Children.Add(Text("If the old device is not really gone but only switched off, its entry then belongs to the wrong machine, and the real one comes up as new the next time it starts. A wrong screen mapping later puts park edge and rotation on the wrong screen - noticed only at the table."));
        panel.Children.Add(Text("This is no restore: the token and the picture store of the old device do not come back."));

        var assign = new Button { Content = "Assign", IsDefault = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };
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
