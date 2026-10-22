using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
/// firewall, so no probe run here can prove the port is reachable from outside - and for the same
/// reason no test can either. What can be established is: the process is listening, which rules
/// exist, what they point at, what they DO, and which networks are live. On a single machine that
/// reading is the entire diagnosis, which is why it has to be right rather than merely present.
/// </para>
/// </summary>
internal sealed class NetworkPanel : StackPanel, IDisposable
{
    /// <summary>
    /// Matches the installed rule and the development one alike - the helper writes
    /// "DnDOverlay Control" or "DnDOverlay Control (dev)" depending on where it sits (Part 9).
    /// </summary>
    private const string RulePrefix = "DnDOverlay Control";

    /// <summary>
    /// Opens the page, never the switch. Changing a network's classification is a machine-wide
    /// setting that every other program feels, it needs an adapter chosen, and an elevated helper
    /// would have to take an argument to do it - so the DM is taken to the right page and decides
    /// there (Part 9).
    /// </summary>
    private const string NetworkSettings = "ms-settings:network-status";

    private readonly HttpClient _probe = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly int _port;
    private readonly TextBlock _listening = Line();
    private readonly TextBlock _addresses = Line();
    private readonly StackPanel _networks = new();
    private readonly StackPanel _rules = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    internal NetworkPanel(int port, bool firstRun)
    {
        _port = port;

        if (firstRun)
        {
            // Only on the very first start, and then never again: it answers the three questions of
            // that one moment. Afterwards the way in is the window (Part 7).
            Children.Add(Heading(Strings.NetworkFirstRunHeading, first: true));
            Children.Add(Steps());
        }

        Children.Add(Heading(Strings.NetworkThisControl, first: !firstRun));
        Children.Add(_addresses);
        Children.Add(_listening);

        Children.Add(Heading(Strings.NetworkNetworks));
        Children.Add(_networks);

        Children.Add(Heading(Strings.NetworkFirewall));
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
            ? Strings.NetworkNoAddress
            : Strings.NetworkReachable(
                string.Join("   ", addresses.Select(address => $"http://{address.Address}:{_port}/")),
                Environment.MachineName);

        ShowFirewall();

        // Last, because it is the only one that waits on anything.
        _listening.Text = await ListeningAsync().ConfigureAwait(true)
            ? Strings.NetworkListening
            : Strings.NetworkNotListening;
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
    /// or is something in the way?</b> Reading needs no elevation, so this runs on opening without
    /// asking anybody for anything (Part 7).
    /// </summary>
    private void ShowFirewall()
    {
        _rules.Children.Clear();
        _networks.Children.Clear();

        var program = Environment.ProcessPath ?? string.Empty;
        var state = Firewall.Inspect(RulePrefix, program);

        if (!state.Asked)
        {
            _networks.Children.Add(Line(Strings.NetworkFirewallNotAsked));
            return;
        }

        var networks = NetworkList.Current();

        ShowNetworks(networks, state.Active);

        var judged = FirewallVerdicts.Judge(state.Rules, state.Active, program);

        ShowRules(judged);
        ShowStandingWarning(judged);
    }

    /// <summary>
    /// One row per connected network, each with its own grip. The DM thinks in networks - "my
    /// table cable", "the guest Wi-Fi" - and the classification that decides everything hangs on
    /// the network, not on the program (Part 7).
    /// </summary>
    private void ShowNetworks(IReadOnlyList<NetworkView> networks, FirewallProfiles active)
    {
        if (networks.Count == 0)
        {
            // The network list could not be read. The profile bitmask still can be, and it is what
            // this view showed before there were rows at all.
            _networks.Children.Add(Line(Strings.NetworkActiveProfiles(Describe(active))));
            _networks.Children.Add(AllowRow(Strings.NetworkAllowInForce, active, networks));

            return;
        }

        foreach (var network in networks)
        {
            _networks.Children.Add(Row(network, networks));
        }
    }

    private StackPanel Row(NetworkView network, IReadOnlyList<NetworkView> networks)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var open = network.Category.HasFlag(FirewallProfiles.Public);

        var label = new TextBlock
        {
            Width = 260,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        label.Inlines.Add(new Run(network.Name + "  "));
        label.Inlines.Add(new Run(Describe(network.Category))
        {
            FontWeight = FontWeights.Bold,

            // Named rather than merely shown: a network Windows has classified as public is the
            // most common reason a rule that IS set does not bite, and somebody looking at this
            // view is usually looking for exactly that (Part 9).
            Foreground = open ? Brushes.Firebrick : label.Foreground,
        });

        row.Children.Add(label);
        row.Children.Add(AllowRow(Strings.NetworkAllowFor(network.Name), network.Category, networks));

        if (open)
        {
            // The better remedy, offered next to the working one: a home network classified as
            // public is the wrong statement about that network for the whole machine, not just
            // for us.
            row.Children.Add(Link(Strings.NetworkChangeClassification));
        }

        return row;
    }

    /// <summary>
    /// The grip that writes the rule. Which of the two helpers is launched follows from the
    /// profiles, so the elevation prompt still names what is about to happen - and the warning is
    /// read before the prompt appears, not after (Part 9).
    /// </summary>
    private System.Windows.Controls.Button AllowRow(
        string caption,
        FirewallProfiles category,
        IReadOnlyList<NetworkView> networks)
    {
        var profiles = FirewallVerdicts.ToWrite(category);
        var anywhere = profiles.HasFlag(FirewallProfiles.Public);

        var button = new System.Windows.Controls.Button
        {
            Content = caption,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        button.Click += (_, _) =>
        {
            var covered = FirewallVerdicts.Covered(networks, profiles);

            // Every network the resulting rule would cover, named. Two networks classified the
            // same cannot be told apart by a profile-scoped rule, so "allow for the cable" may well
            // cover the Wi-Fi too - and this is where that is said instead of implied.
            var message = covered.Count > 1
                ? Strings.NetworkPublicMessageCovering(string.Join(", ", covered.Select(network => network.Name)))
                : Strings.NetworkPublicMessage;

            if (anywhere && !Confirm(Strings.NetworkPublicTitle, message))
            {
                return;
            }

            Elevate(anywhere ? "DnDOverlay.FirewallAddAnywhere.exe" : "DnDOverlay.FirewallAdd.exe");
        };

        return button;
    }

    private void ShowRules(IReadOnlyList<JudgedRule> judged)
    {
        var blocking = judged.Count(rule => rule.Verdict == FirewallVerdict.Blocks);

        if (blocking > 0)
        {
            // First, and before any counting: a block beats every allow beside it, so how many
            // rules there are is the second question.
            _rules.Children.Add(Line(Strings.NetworkRuleBlocks, Brushes.Firebrick));
        }
        else if (judged.Count == 0)
        {
            _rules.Children.Add(Line(Strings.NetworkNoRule));

            return;
        }
        else if (!FirewallVerdicts.GetsThrough(judged))
        {
            _rules.Children.Add(Line(Strings.NetworkNoneLetsThrough, Brushes.Firebrick));
        }
        else if (judged.Count > 1)
        {
            _rules.Children.Add(Line(Strings.NetworkRulesFound(judged.Count)));
        }

        foreach (var rule in judged)
        {
            _rules.Children.Add(Describe(rule));
        }
    }

    /// <summary>
    /// A firewall rule cannot expire, so the only honest substitute for "just this once" is a
    /// statement that keeps standing. Shown every time the view is opened, for as long as the rule
    /// is there (Part 9).
    /// </summary>
    private void ShowStandingWarning(IReadOnlyList<JudgedRule> judged)
    {
        var open = judged.Any(rule =>
            rule.Rule.Action == FirewallAction.Allow
            && rule.Verdict != FirewallVerdict.OtherProgram
            && rule.Rule.Profiles.HasFlag(FirewallProfiles.Public));

        if (!open)
        {
            return;
        }

        _rules.Children.Add(Line(Strings.NetworkPublicStanding, Brushes.DarkOrange));
    }

    /// <summary>
    /// Four facts per rule, and the one judgement that matters: what does it do right now? A rule
    /// can be present, enabled, for the right program and still be the reason nothing works.
    /// </summary>
    private static TextBlock Describe(JudgedRule judged)
    {
        var verdict = judged.Verdict switch
        {
            FirewallVerdict.Allows => Strings.NetworkVerdictAllows,
            FirewallVerdict.Blocks => Strings.NetworkVerdictBlocks,
            FirewallVerdict.Overruled => Strings.NetworkVerdictOverruled,
            FirewallVerdict.OtherProfile => Strings.NetworkVerdictOtherProfile,
            FirewallVerdict.Disabled => Strings.NetworkVerdictDisabled,
            _ => Strings.NetworkVerdictOtherProgram,
        };

        var colour = judged.Verdict switch
        {
            FirewallVerdict.Allows => Brushes.DarkGreen,
            FirewallVerdict.Blocks or FirewallVerdict.Overruled => Brushes.Firebrick,
            _ => Brushes.DimGray,
        };

        var rule = judged.Rule;
        var action = rule.Action == FirewallAction.Allow ? Strings.NetworkActionAllow : Strings.NetworkActionBlock;

        return new TextBlock
        {
            Text = $"    {rule.Name}  [{action}, {Describe(rule.Profiles)}]  {verdict}"
                + $"\n        {rule.Program ?? Strings.NetworkNoProgram}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
            Foreground = colour,
        };
    }

    private static string Describe(FirewallProfiles profiles)
    {
        if (profiles == FirewallProfiles.All)
        {
            return Strings.NetworkAllProfiles;
        }

        var named = new[] { FirewallProfiles.Domain, FirewallProfiles.Private, FirewallProfiles.Public }
            .Where(profile => profiles.HasFlag(profile))
            .Select(Name)
            .ToList();

        return named.Count > 0 ? string.Join(", ", named) : Strings.NetworkNoProfile;

        static string Name(FirewallProfiles profile) => profile switch
        {
            FirewallProfiles.Domain => Strings.NetworkProfileDomain,
            FirewallProfiles.Private => Strings.NetworkProfilePrivate,
            _ => Strings.NetworkProfilePublic,
        };
    }

    private StackPanel Buttons()
    {
        var clear = new System.Windows.Controls.Button
        {
            Content = Strings.NetworkRemoveAll,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        clear.Click += (_, _) =>
        {
            // Said in full before anything is elevated. Removal goes by program path, so it takes
            // ours, the ones Windows wrote, and anything built by hand - and "remove" is read as
            // "remove" rather than qualified afterwards.
            if (Confirm(Strings.NetworkRemoveTitle, Strings.NetworkRemoveMessage(Environment.ProcessPath)))
            {
                Elevate("DnDOverlay.FirewallRemove.exe");
            }
        };

        var again = new System.Windows.Controls.Button
        {
            Content = Strings.NetworkCheckAgain,
            Padding = new Thickness(12, 6, 12, 6),
        };

        again.Click += (_, _) => Refresh();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

        row.Children.Add(clear);
        row.Children.Add(again);

        return row;
    }

    private static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            == MessageBoxResult.OK;

    /// <summary>
    /// Starts one of the three helpers elevated. <c>runas</c> rather than a call of our own, and
    /// separate programs rather than one with modes, so that <b>the prompt names what is about to
    /// happen</b> (Part 9).
    /// </summary>
    private void Elevate(string helper)
    {
        var path = Path.Combine(AppContext.BaseDirectory, helper);

        if (!File.Exists(path))
        {
            _status.Text = Strings.NetworkHelperMissing(helper);
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
                ? Strings.NetworkHelperNotStarted
                : Strings.NetworkHelperFinished(helper, elevated.ExitCode);
        }
        catch (Win32Exception)
        {
            // The elevation prompt was declined. Not a failure of ours, and expressly not fatal:
            // everything else in this view goes on working, and the netsh command is in the README.
            _status.Text = Strings.NetworkElevationDeclined;
        }

        Refresh();
    }

    private System.Windows.Controls.Button Link(string caption)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = caption,
            Padding = new Thickness(12, 6, 12, 6),
        };

        button.Click += (_, _) =>
        {
            try
            {
                using var settings = Process.Start(new ProcessStartInfo(NetworkSettings)
                {
                    UseShellExecute = true,
                });
            }
            catch (Win32Exception)
            {
                _status.Text = Strings.NetworkSettingsNotOpened;
            }
        };

        return button;
    }

    private static StackPanel Steps()
    {
        var steps = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        foreach (var step in new[]
        {
            Strings.NetworkStepInstall,
            Strings.NetworkStepRequest,
            Strings.NetworkStepAllow,
        })
        {
            steps.Children.Add(Line(step));
        }

        steps.Children.Add(Line(Strings.NetworkDisplayPcNote));

        return steps;
    }

    private static TextBlock Heading(string text, bool first = false) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, first ? 0 : 14, 0, 4),
        };

    private static TextBlock Line(string text = "", Brush? foreground = null)
    {
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };

        if (foreground is not null)
        {
            line.Foreground = foreground;
        }

        return line;
    }

    public void Dispose() => _probe.Dispose();
}
