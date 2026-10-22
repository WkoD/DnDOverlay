using System.Windows;
using System.Windows.Controls;

namespace DnDOverlay.Control;

/// <summary>
/// What the window's X asks (Part 7): quit, with the two switches that make quitting the moment a
/// layout is kept and the tables are freed - or cancel. <b>"Save layout" is off</b>, or the tab would
/// fill with stands nobody tells apart; <b>"Release the screens" is on while devices are connected</b>,
/// because a table left lit after the DM went home belongs to nobody. "To the notification area" joins
/// it with the tray (M6b).
/// </summary>
internal sealed class QuitDialog : Window
{
    private readonly CheckBox _save = new() { Content = Strings.QuitSaveLayout, IsChecked = false, Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _release = new() { Content = Strings.QuitReleaseScreens, Margin = new Thickness(0, 0, 0, 4) };

    private QuitDialog(Window owner, bool campaignOpen, bool devicesConnected)
    {
        Owner = owner;
        Title = Strings.QuitTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        // A layout needs a campaign to go into; without one the switch says why it cannot.
        _save.IsEnabled = campaignOpen;
        _release.IsChecked = devicesConnected;

        var quit = new Button { Content = Strings.QuitQuit, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Strings.QuitCancel, Padding = new Thickness(14, 3, 14, 3), IsCancel = true };

        quit.Click += (_, _) => DialogResult = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };

        buttons.Children.Add(quit);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 460 };

        panel.Children.Add(new TextBlock { Text = Strings.QuitQuestion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(_save);

        if (!campaignOpen)
        {
            panel.Children.Add(new TextBlock { Text = Strings.QuitNoCampaign, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(20, 0, 0, 6) });
        }

        panel.Children.Add(_release);
        panel.Children.Add(new TextBlock { Text = Strings.QuitReleaseNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(20, 0, 0, 0) });
        panel.Children.Add(buttons);

        Content = panel;
    }

    /// <summary>Asks; <see langword="null"/> when the DM cancelled.</summary>
    internal static (bool SaveLayout, bool ReleaseScreens)? Ask(Window owner, bool campaignOpen, bool devicesConnected)
    {
        var dialog = new QuitDialog(owner, campaignOpen, devicesConnected);

        return dialog.ShowDialog() is true
            ? (dialog._save.IsChecked is true, dialog._release.IsChecked is true)
            : null;
    }
}
