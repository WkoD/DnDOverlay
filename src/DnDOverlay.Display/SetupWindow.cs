using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DnDOverlay.Platform.Windows;

namespace DnDOverlay.Display;

/// <summary>
/// The setup screen (Part 6, Part 4): while a pairing request lies before the DM - and exactly then -
/// the device shows its name, its address and the pairing code, large enough to read from two metres,
/// so the DM can compare them with the line in the control without walking.
/// <para>
/// <b>Not before, and not after a refusal.</b> A device that only searches starts quietly; a
/// full-screen box at every login would be a trap on an ordinary Tuesday. A refusal ends the
/// connection and takes the screen with it, and the retries five minutes apart do not bring it back.
/// </para>
/// <para>
/// <b>It shows, it does not ask</b>: nothing on it can be operated, so it takes no focus and no input -
/// the decision is made at the control.
/// </para>
/// </summary>
internal sealed class SetupWindow : Window
{
    private readonly MonitorInfo _monitor;

    internal SetupWindow(MonitorInfo monitor, string name, string code)
    {
        _monitor = monitor;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x18));
        Title = Strings.SetupTitle;

        var lines = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        lines.Children.Add(Line(Strings.SetupWaiting, 18, 0.7));
        lines.Children.Add(Line(name, 48, 1));
        lines.Children.Add(Line(LocalAddress() ?? Strings.SetupAddressUnknown, 22, 0.85));
        lines.Children.Add(Line(Strings.SetupCode(code), 64, 1, margin: 24));
        lines.Children.Add(Line(Strings.SetupHint, 16, 0.7, margin: 18));

        // Scaled with the screen: two metres from a 65-inch table and from a 13-inch tablet alike.
        Content = new Viewbox
        {
            Child = lines,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(80),
            MaxWidth = double.PositiveInfinity,
        };

        SourceInitialized += (_, _) => Place();
    }

    /// <summary>
    /// This machine's address on the network as a person would type it - the first IPv4 address of an
    /// interface that is up, not a loopback and not a link-local one. A guess with several networks,
    /// and therefore stated as what it is: the address the control sees stands beside the code there.
    /// </summary>
    internal static string? LocalAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address)
                    && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                ?.ToString();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;

        if (handle == 0)
        {
            return;
        }

        var (x, y, width, height) = _monitor.Bounds;

        Native.SetWindowPos(handle, insertAfter: 0, x, y, width, height, Native.SwpNoZOrder | Native.SwpNoActivate);
    }

    private static TextBlock Line(string text, double size, double opacity, double margin = 6) =>
        new()
        {
            Text = text,
            FontSize = size,
            Foreground = Brushes.White,
            Opacity = opacity,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, margin, 0, 0),
        };
}
