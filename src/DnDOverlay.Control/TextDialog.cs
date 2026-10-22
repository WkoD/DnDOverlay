using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// Asks for the words of a free text (M5b9) - several lines allowed, Enter makes a new one, the
/// button lays it down. Empty is no text: nothing is laid down for it.
/// </summary>
internal sealed class TextDialog : Window
{
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 70,
        MaxLength = TextItem.MaxLength,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private TextDialog(Window? owner, string? current)
    {
        Owner = owner;
        Title = current is null ? Strings.TextDialogAddTitle : Strings.TextDialogEditTitle;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _text.Text = current ?? string.Empty;

        var lay = new Button { Content = current is null ? Strings.TextDialogAdd : Strings.TextDialogChange, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Strings.TextDialogCancel, Padding = new Thickness(14, 3, 14, 3), IsCancel = true };

        lay.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_text.Text))
            {
                DialogResult = true;
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };

        buttons.Children.Add(lay);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock { Text = Strings.TextDialogPrompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(_text);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) =>
        {
            _text.Focus();
            _text.SelectAll();
        };
    }

    /// <summary>The words, or <see langword="null"/> when the DM cancelled.</summary>
    internal static string? Ask(Window? owner, string? current)
    {
        var dialog = new TextDialog(owner, current);

        return dialog.ShowDialog() is true ? dialog._text.Text.Trim() : null;
    }
}
