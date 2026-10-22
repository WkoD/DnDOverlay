using System.Windows;
using System.Windows.Controls;
using DnDOverlay.Campaign;
using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>What the mapping table decided for one scene of a layout.</summary>
/// <param name="Part">The scene, with the screen it was saved from.</param>
/// <param name="Onto">The screen it goes onto - none when it is saved as a scene or left out.</param>
/// <param name="AsScene">Saved as a scene of its own instead of being laid.</param>
internal sealed record LayoutChoice(LayoutPart Part, ScreenRef? Onto, bool AsScene);

/// <summary>
/// The mapping table (Part 3): shown only when a layout's screens are not all there, one row per scene,
/// preset to the screen that was found. Three ways per row - onto a screen that is there ("the beamer is
/// not here today, put it on the table"), saved as a scene of its own, or not loaded.
/// <para>
/// <b>Loading in part destroys nothing</b>: the layout file stays whole, and when the beamer stands
/// there again next week the same layout loads without the table.
/// </para>
/// </summary>
internal sealed class LayoutMappingWindow : Window
{
    private const string AsScene = "scene";
    private const string Skip = "skip";

    private readonly List<(LayoutPart Part, ComboBox Choice)> _rows = [];

    private LayoutMappingWindow(
        Window owner,
        string name,
        IReadOnlyList<LayoutPart> parts,
        IReadOnlyList<ScreenRef?> found,
        IReadOnlyList<ScreenView> screens,
        Pictures pictures)
    {
        Owner = owner;
        Title = Strings.LayoutMappingTitle;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock
        {
            Text = Strings.LayoutMappingIntro(name),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        for (var index = 0; index < parts.Count; index++)
        {
            panel.Children.Add(Row(parts[index], found[index], screens, pictures));
        }

        var load = new Button { Content = Strings.LayoutMappingLoad, IsDefault = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Strings.LayoutMappingCancel, IsCancel = true, Padding = new Thickness(16, 4, 16, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };

        buttons.Children.Add(load);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        load.Click += (_, _) => DialogResult = true;

        Content = panel;
    }

    /// <summary>Asks, and answers with a choice per scene in the layout's order - or nothing when the DM cancelled.</summary>
    internal static IReadOnlyList<LayoutChoice>? Ask(
        Window owner,
        string name,
        IReadOnlyList<LayoutPart> parts,
        IReadOnlyList<ScreenRef?> found,
        IReadOnlyList<ScreenView> screens,
        Pictures pictures)
    {
        var window = new LayoutMappingWindow(owner, name, parts, found, screens, pictures);

        if (window.ShowDialog() != true)
        {
            return null;
        }

        return [.. window._rows.Select(row => (row.Choice.SelectedItem as ComboBoxItem)?.Tag switch
        {
            ScreenRef screen => new LayoutChoice(row.Part, screen, AsScene: false),
            AsScene => new LayoutChoice(row.Part, null, AsScene: true),
            _ => new LayoutChoice(row.Part, null, AsScene: false),
        })];
    }

    private DockPanel Row(LayoutPart part, ScreenRef? found, IReadOnlyList<ScreenView> screens, Pictures pictures)
    {
        // The scene in its saved shape - the DM recognises what lay there faster than a screen name.
        var origin = part.Scene.Origin;
        var aspect = origin.Height > 0 ? (double)origin.Width / origin.Height : 16d / 9;
        var preview = new SceneThumbnail(pictures)
        {
            Width = aspect >= 1 ? 96 : 96 * aspect,
            Height = aspect >= 1 ? 96 / aspect : 96,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        preview.Show(part.Scene.Scene, ScreenContext.Default(origin, 96), default);
        preview.InvalidateVisual();

        var choice = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };

        foreach (var view in screens)
        {
            choice.Items.Add(new ComboBoxItem { Content = view.Info.Label, Tag = view.Screen });
        }

        choice.Items.Add(new ComboBoxItem { Content = Strings.LayoutMappingAsScene, Tag = AsScene });
        choice.Items.Add(new ComboBoxItem { Content = Strings.LayoutMappingSkip, Tag = Skip });

        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().First(item =>
            found is { } screen ? item.Tag is ScreenRef target && target == screen : Equals(item.Tag, Skip));

        _rows.Add((part, choice));

        var label = new TextBlock
        {
            Text = found is null ? Strings.LayoutMappingNotHere(part.ScreenName) : part.ScreenName,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

        DockPanel.SetDock(preview, Dock.Left);
        DockPanel.SetDock(choice, Dock.Right);
        row.Children.Add(preview);
        row.Children.Add(choice);
        row.Children.Add(label);

        return row;
    }
}
