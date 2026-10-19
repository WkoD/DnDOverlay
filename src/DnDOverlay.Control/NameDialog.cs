using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DnDOverlay.Campaign;

namespace DnDOverlay.Control;

/// <summary>
/// Asks for a name that becomes a file name - saving a scene, renaming one (Part 3). The suggestion
/// stands in the field and can be changed; what comes out is checked, never rewritten, and a reason
/// is said for what cannot pass.
/// </summary>
internal sealed class NameDialog : Window
{
    private readonly TextBox _name;
    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    private NameDialog(Window owner, string title, string prompt, string suggestion)
    {
        Owner = owner;
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _name = new TextBox { Text = suggestion, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(4) };

        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 4, 16, 4) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_name);
        panel.Children.Add(_problem);
        panel.Children.Add(buttons);

        Content = panel;

        ok.Click += (_, _) =>
        {
            if (FileNames.Check(_name.Text) is { } problem)
            {
                _problem.Text = Explain(problem);
                return;
            }

            DialogResult = true;
        };

        _name.TextChanged += (_, _) => _problem.Text = string.Empty;

        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
            Keyboard.Focus(_name);
        };
    }

    /// <summary>Asks, and answers with the typed name - or nothing when the DM cancelled.</summary>
    internal static string? Ask(Window owner, string title, string prompt, string suggestion)
    {
        var dialog = new NameDialog(owner, title, prompt, suggestion);

        return dialog.ShowDialog() == true ? dialog._name.Text : null;
    }

    /// <summary>What stands in the way, said so the DM knows what to change.</summary>
    internal static string Explain(NameProblem problem) => problem switch
    {
        NameProblem.Empty => "A name is needed.",
        NameProblem.ForbiddenCharacter => "A name cannot contain < > : \" / \\ | ? * - Windows does not allow them in file names.",
        NameProblem.ReservedName => "CON, PRN, AUX, NUL, COM1-9 and LPT1-9 are reserved by Windows.",
        NameProblem.BadEnding => "A name cannot end in a dot or a space.",
        NameProblem.TooLong => $"A name can have at most {FileNames.MaxLength} characters.",
        _ => problem.ToString(),
    };
}
