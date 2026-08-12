using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DnDOverlay.Hub;
using DnDOverlay.Platform.Windows;

namespace DnDOverlay.Control;

/// <summary>
/// Where this control can be reached, and whether anything is in the way. It is the content of the
/// first-run view and of the network window alike - one implementation, two ways in (Part 7).
/// <para>
/// <b>What it can honestly say is bounded</b>, and the wording keeps to it. A request to our own
/// LAN address from this very machine is routed over loopback and never touches the inbound
/// firewall, so no probe run here can prove the port is reachable from outside. What can be
/// established is: the process is listening, which rules exist, what they point at, and which
/// profile is live - and that combination is what actually answers "will a display get through?".
/// </para>
/// </summary>
internal sealed class NetworkPanel : StackPanel, IDisposable
{
    /// <summary>
    /// Matches the installed rule and the development one alike - the helper writes
    /// "DnDOverlay Control" or "DnDOverlay Control (dev)" depending on where it sits (Part 9).
    /// </summary>
    private const string RulePrefix = "DnDOverlay Control";

    private readonly HttpClient _probe = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly int _port;
    private readonly TextBlock _listening = Line();
    private readonly TextBlock _profile = Line();
    private readonly TextBlock _addresses = Line();
    private readonly StackPanel _rules = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    internal NetworkPanel(int port, bool firstRun)
    {
        _port = port;

        if (firstRun)
        {
            // Only on the very first start, and then never again: it answers the three questions of
            // that one moment. Afterwards the way in is the window (Part 7).
            Children.Add(Heading("No device yet - here is the way there", first: true));
            Children.Add(Steps());
        }

        Children.Add(Heading("This control", first: !firstRun));
        Children.Add(_addresses);
        Children.Add(_listening);
        Children.Add(_profile);

        Children.Add(Heading("Firewall"));
        Children.Add(_rules);
        Children.Add(Buttons());
        Children.Add(_status);

        Refresh();
    }

    /// <summary>
    /// Reads everything again. Called on opening and after every grip, because the whole point of
    /// the second button is that the result of the first one can be seen.
    /// </summary>
    internal async void Refresh()
    {
        var addresses = LocalAddresses.Preferred();

        _addresses.Text = addresses.Count == 0
            ? "No network address - this machine is reachable over loopback only."
            : "Reachable at " + string.Join(
                "   ",
                addresses.Select(address => $"http://{address.Address}:{_port}/"))
                + $"   (machine name: {Environment.MachineName})";

        ShowFirewall();

        // Last, because it is the only one that waits on anything.
        _listening.Text = await ListeningAsync().ConfigureAwait(true)
            ? "The hub is listening. That says the process is up - it cannot say the port is open, "
                + "because a request from this machine to its own address never passes the firewall."
            : "The hub is NOT answering on this machine. Nothing else here matters until it does.";
    }

    private async Task<bool> ListeningAsync()
    {
        try
        {
            using var answer = await _probe
                .GetAsync(new Uri($"http://127.0.0.1:{_port}{Core.Protocol.Protocol.HealthPath}"))
                .ConfigureAwait(true);

            return answer.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// The part that answers the question one otherwise has to guess at: <b>does the new rule bite,
    /// or is an old one still in the way?</b> Reading needs no elevation, so this runs on opening
    /// without asking anybody for anything (Part 7).
    /// </summary>
    private void ShowFirewall()
    {
        _rules.Children.Clear();

        var program = Environment.ProcessPath ?? string.Empty;
        var state = Firewall.Inspect(RulePrefix, program);

        if (!state.Asked)
        {
            _profile.Text = "The firewall could not be asked - the service may be off.";
            return;
        }

        _profile.Inlines.Clear();
        _profile.Inlines.Add(new System.Windows.Documents.Run("Active network profile: "));
        _profile.Inlines.Add(new System.Windows.Documents.Run(Describe(state.Active))
        {
            FontWeight = FontWeights.Bold,

            // Named rather than merely shown: a network Windows has classified as public is the
            // most common reason a rule that IS set does not bite, and somebody looking at this
            // view is usually looking for exactly that (Part 9).
            Foreground = state.Active.HasFlag(FirewallProfiles.Public) ? Brushes.Firebrick : _profile.Foreground,
        });

        if (state.Rules.Count == 0)
        {
            _rules.Children.Add(Line("No rule of ours, and none for this program. A display on "
                + "another machine will not get through until one is set."));
            return;
        }

        _rules.Children.Add(Line(state.Rules.Count == 1
            ? "One rule found:"
            : $"{state.Rules.Count} rules found - more than one is worth clearing up:"));

        foreach (var rule in state.Rules)
        {
            _rules.Children.Add(Describe(rule, state.Active, program));
        }
    }

    /// <summary>
    /// Three facts per rule, and the one judgement that matters: does it apply <i>right now</i>?
    /// A rule can be present, enabled and still be for a profile nobody is in.
    /// </summary>
    private static TextBlock Describe(FirewallRuleView rule, FirewallProfiles active, string program)
    {
        var mine = string.Equals(rule.Program, program, StringComparison.OrdinalIgnoreCase);
        var bites = rule.Enabled && mine && (rule.Profiles & active) != 0;

        var verdict = bites
            ? "applies now"
            : !mine
                ? "points somewhere else - probably left over"
                : !rule.Enabled
                    ? "disabled"
                    : "does not cover the active profile";

        return new TextBlock
        {
            Text = $"    {rule.Name}  [{Describe(rule.Profiles)}]  {verdict}\n        {rule.Program ?? "(no program)"}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
            Foreground = bites ? Brushes.DarkGreen : Brushes.DimGray,
        };
    }

    private static string Describe(FirewallProfiles profiles)
    {
        if (profiles == FirewallProfiles.All)
        {
            return "all profiles";
        }

        var named = new[] { FirewallProfiles.Domain, FirewallProfiles.Private, FirewallProfiles.Public }
            .Where(profile => profiles.HasFlag(profile))
            .Select(profile => profile.ToString().ToLowerInvariant())
            .ToList();

        return named.Count > 0 ? string.Join(", ", named) : "no profile";
    }

    private StackPanel Buttons()
    {
        var set = Button("Set the firewall rule", "DnDOverlay.FirewallAdd.exe");
        var clear = Button("Remove the firewall rule", "DnDOverlay.FirewallRemove.exe");
        var again = new System.Windows.Controls.Button
        {
            Content = "Check again",
            Padding = new Thickness(12, 6, 12, 6),
        };

        again.Click += (_, _) => Refresh();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

        row.Children.Add(set);
        row.Children.Add(clear);
        row.Children.Add(again);

        return row;
    }

    /// <summary>
    /// Starts one of the two helpers elevated. <c>runas</c> rather than a call of our own, and two
    /// programs rather than one with a mode, so that <b>the prompt names what is about to
    /// happen</b> (Part 9).
    /// </summary>
    private System.Windows.Controls.Button Button(string caption, string helper)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = caption,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        button.Click += (_, _) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, helper);

            if (!File.Exists(path))
            {
                _status.Text = $"{helper} is not next to this program. The rule can be set by hand - "
                    + "the netsh command is in the README.";
                return;
            }

            try
            {
                using var elevated = Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });

                elevated?.WaitForExit();

                _status.Text = elevated is null
                    ? "The helper did not start."
                    : string.Create(CultureInfo.InvariantCulture, $"{helper} finished with exit code {elevated.ExitCode}.");
            }
            catch (Win32Exception)
            {
                // The elevation prompt was declined. Not a failure of ours, and expressly not
                // fatal: everything else in this view goes on working, and the netsh command is
                // in the README.
                _status.Text = "The elevation prompt was declined - nothing was changed.";
            }

            Refresh();
        };

        return button;
    }

    private static StackPanel Steps()
    {
        var steps = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        foreach (var step in new[]
        {
            "1.  Run the display MSI on the display PC - no administrator rights needed.",
            "2.  It finds this control by itself and appears below as a request.",
            "3.  Allow it and compare the code with what stands on the table.",
        })
        {
            steps.Children.Add(Line(step));
        }

        steps.Children.Add(Line("Autologon, no lock screen, edge swipes off and Windows Update "
            + "active hours - the four things on the display PC that the README explains."));

        return steps;
    }

    private static TextBlock Heading(string text, bool first = false) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, first ? 0 : 14, 0, 4),
        };

    private static TextBlock Line(string text = "") =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };

    public void Dispose() => _probe.Dispose();
}
