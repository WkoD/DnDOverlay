using System.Windows;
using System.Windows.Controls;

namespace DnDOverlay.Control;

/// <summary>
/// The second way into <see cref="NetworkPanel"/>, and the reason there is one at all.
/// <para>
/// The first-run view answers the questions of the very first start and then has served its
/// purpose (Part 7) - but the grips in it are needed again later, and at the worst moment: a
/// firewall rule stops biting <b>without disappearing</b> when Windows reclassifies a network as
/// public. A button that existed only at first run would be gone exactly when it is wanted.
/// </para>
/// <para>
/// In M6 this moves into the settings window, where Part 7 puts it. Until then it is a window of
/// its own, because the settings window does not exist yet and the two-machine run of M1c needs
/// the grips.
/// </para>
/// </summary>
internal sealed class NetworkWindow : Window, IDisposable
{
    private readonly NetworkPanel _panel;

    internal NetworkWindow(int port)
    {
        _panel = new NetworkPanel(port, firstRun: false) { Margin = new Thickness(16) };

        Title = "Network";
        Width = 720;
        Height = 480;

        Content = new ScrollViewer
        {
            Content = _panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        Closed += (_, _) => Dispose();
    }

    public void Dispose() => _panel.Dispose();
}
