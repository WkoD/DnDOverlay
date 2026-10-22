using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DnDOverlay.Core;

namespace DnDOverlay.Control;

/// <summary>
/// The tidy-up view (Part 7): the open campaign's pictures with the room each takes, largest first,
/// "Check folder again" and "Verify stock". <b>Only the open campaign</b> - a closed one is not
/// looked into; whoever wants to tidy it opens it, and then its folder check has run anyway.
/// <para>
/// <b>Sizes come from the inventory, not the file system</b>: the numbers are in memory, so the view
/// asks no folder of thousands of files. <b>Nothing is deleted here</b> - deleting happens in the
/// stock, the one index of the pictures (Part 3); a double click shows the picture there.
/// </para>
/// </summary>
internal sealed class TidyWindow : Window
{
    private readonly CampaignDesk _desk;
    private readonly Action<AssetId> _showInStock;
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListView _list = new() { MinHeight = 200 };
    private readonly Button _rescan = new() { Content = Strings.TidyRescan, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _verify = new() { Content = Strings.TidyVerify, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _breakOff = new() { Content = Strings.TidyBreakOff, Padding = new Thickness(10, 3, 10, 3), Visibility = Visibility.Collapsed };
    private readonly ProgressBar _progress = new() { Height = 14, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    private CancellationTokenSource? _breaking;

    internal TidyWindow(Window owner, CampaignDesk desk, Action<AssetId> showInStock)
    {
        _desk = desk;
        _showInStock = showInStock;

        Owner = owner;
        Title = Strings.TidyTitle;
        Width = 720;
        Height = 640;

        var view = new GridView();

        view.Columns.Add(Column(Strings.TidyColumnName, nameof(Row.Name), 280));
        view.Columns.Add(Column(Strings.TidyColumnSize, nameof(Row.Size), 90));
        view.Columns.Add(Column(Strings.TidyColumnPixels, nameof(Row.Pixels), 110));
        view.Columns.Add(Column(Strings.TidyColumnUsed, nameof(Row.Used), 110));
        _list.View = view;
        _list.MouseDoubleClick += (_, _) =>
        {
            if (_list.SelectedItem is Row row)
            {
                _showInStock(row.Asset);
                owner.Activate();
            }
        };

        _rescan.Click += async (_, _) => await RescanAsync().ConfigureAwait(true);
        _verify.Click += async (_, _) => await VerifyAsync().ConfigureAwait(true);
        _breakOff.Click += (_, _) => _breaking?.Cancel();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

        buttons.Children.Add(_rescan);
        buttons.Children.Add(_verify);
        buttons.Children.Add(_breakOff);

        var foot = new StackPanel();

        foot.Children.Add(new TextBlock { Text = Strings.TidyDeleteNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 8, 0, 0) });
        foot.Children.Add(buttons);
        foot.Children.Add(_progress);
        foot.Children.Add(_result);

        var layout = new DockPanel { Margin = new Thickness(16) };

        DockPanel.SetDock(_summary, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        layout.Children.Add(_summary);
        layout.Children.Add(foot);
        layout.Children.Add(_list);

        Content = layout;

        // Not modal, so it stays open while the DM deletes, renames or takes pictures in - and the
        // sizes and names it lists follow, or a double click would look for a picture that is gone.
        _desk.Changed += Refreshed;
        _desk.StockChanged += Refreshed;
        Closed += (_, _) =>
        {
            _desk.Changed -= Refreshed;
            _desk.StockChanged -= Refreshed;
            _breaking?.Cancel();
        };

        Refresh();
    }

    private void Refreshed(object? sender, EventArgs e) => Refresh();

    /// <summary>The numbers of the open campaign, from the inventory in memory; a window without one closes.</summary>
    private void Refresh()
    {
        if (_desk.Open is not { } open)
        {
            Close();
            return;
        }

        var used = open.Shelf.UseCounts();
        var rows = open.Store.Entries
            .OrderByDescending(entry => entry.Bytes)
            .Select(entry => new Row(
                new AssetId(entry.AssetId),
                entry.Name,
                Strings.TidySize(entry.Bytes / (1024d * 1024)),
                Strings.TidyPixels(entry.PixelWidth, entry.PixelHeight),
                used.GetValueOrDefault(new AssetId(entry.AssetId)) is var count and > 0 ? Strings.TidyUsedIn(count) : Strings.TidyUnused))
            .ToList();

        _list.ItemsSource = rows;
        _summary.Text = Strings.TidySummary(
            _desk.OpenLabel ?? open.Name,
            open.Store.Count,
            open.Store.Entries.Sum(entry => entry.Bytes) / (1024d * 1024),
            _desk.FreeSpace() / (1024d * 1024 * 1024));
    }

    /// <summary>"Check folder again" - the name match of the opening once more, its report in the box beside the stage.</summary>
    private async Task RescanAsync()
    {
        _rescan.IsEnabled = false;
        _result.Text = Strings.TidyRescanning;

        await _desk.RescanAsync().ConfigureAwait(true);

        _result.Text = Strings.TidyRescanned;
        _rescan.IsEnabled = true;
        Refresh();
    }

    /// <summary>
    /// "Verify stock" - every byte, with progress and a way to break off, because it is minutes on a
    /// large stock. Also started from the offer after a move of the campaign folder; one at a time.
    /// </summary>
    internal async Task VerifyAsync()
    {
        if (_desk.Open is not { } open || _breaking is not null)
        {
            return;
        }

        using var breaking = new CancellationTokenSource();

        _breaking = breaking;
        _verify.IsEnabled = false;
        _rescan.IsEnabled = false;
        _breakOff.Visibility = Visibility.Visible;
        _progress.Visibility = Visibility.Visible;
        _progress.Maximum = Math.Max(1, open.Store.Count);
        _progress.Value = 0;
        _result.Text = Strings.TidyVerifying;

        try
        {
            var result = await _desk.VerifyAsync(new Progress<int>(done => _progress.Value = done), breaking.Token).ConfigureAwait(true);
            var names = open.Store.Entries.ToDictionary(entry => entry.AssetId, entry => entry.Name, StringComparer.Ordinal);

            _result.Text = result.Damaged.Count == 0
                ? Strings.TidyVerifiedAllWell(result.Checked)
                : Strings.TidyVerifiedDamaged(
                    result.Checked,
                    result.Damaged.Count,
                    string.Join(", ", result.Damaged.Select(asset => names.GetValueOrDefault(asset.Value, asset.Value))));
        }
        catch (OperationCanceledException)
        {
            _result.Text = Strings.TidyVerifyBrokenOff;
        }
        finally
        {
            _breaking = null;
            _verify.IsEnabled = true;
            _rescan.IsEnabled = true;
            _breakOff.Visibility = Visibility.Collapsed;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private static GridViewColumn Column(string header, string path, double width) =>
        new() { Header = header, DisplayMemberBinding = new Binding(path), Width = width };

    /// <summary>One picture as the list shows it.</summary>
    private sealed record Row(AssetId Asset, string Name, string Size, string Pixels, string Used);
}
