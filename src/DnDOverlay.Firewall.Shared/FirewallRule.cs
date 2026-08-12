using System.Diagnostics;
using System.Globalization;

namespace DnDOverlay.Firewall;

/// <summary>
/// What the two helpers share: which rule they are talking about, and how a <c>netsh</c> call is
/// made.
/// <para>
/// It is a linked source file rather than a library on purpose. Each helper is meant to be one
/// self-contained executable whose <b>name</b> is the whole message the UAC prompt carries; a
/// second assembly beside it would buy nothing and would have to be deployed with it.
/// </para>
/// </summary>
internal static class FirewallRule
{
    /// <summary>
    /// The program the rule points at. The helper sits next to it - the installer puts it there,
    /// and the development build copies it there - so "my own directory" is the answer, and
    /// nothing has to be passed in.
    /// </summary>
    internal const string Program = "DnDOverlay.Control.exe";

    /// <summary>
    /// A rule is PROGRAM-based, never port-based, because the port is configurable: a rule nailed
    /// to 47800 would be worthless after the first port change (Part 9).
    /// </summary>
    internal static string Path => System.IO.Path.Combine(Directory, Program);

    /// <summary>
    /// Private and domain, never public. This is the most common reason a rule that IS set does
    /// not bite: Windows likes to classify a freshly joined network as "Public", and the control's
    /// reachability view therefore shows the active profile alongside (Part 7, Part 9).
    /// </summary>
    internal const string Profiles = "private,domain";

    /// <summary>
    /// Two names, and the difference is not cosmetic. Without it every development run would leave
    /// a rule behind pointing at a path that no longer exists, and half a year later nobody could
    /// say which of the three rules is the live one.
    /// </summary>
    internal static string Name => Installed ? "DnDOverlay Control" : "DnDOverlay Control (dev)";

    /// <summary>
    /// Decided HERE and never by the caller. A program that starts elevated and lets an
    /// unprivileged caller tell it <b>what</b> to do is a privilege escalation with a covering
    /// letter - which is also why neither helper takes arguments at all (Part 9).
    /// </summary>
    internal static bool Installed =>
        Directory.StartsWith(
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "DnDOverlay"),
            StringComparison.OrdinalIgnoreCase);

    private static string Directory =>
        System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>
    /// Removes every rule of our name. Called on its own by the remove helper, and first by the
    /// add helper.
    /// </summary>
    /// <returns>
    /// The exit code, swallowed by the caller where "there was nothing to remove" is a fine
    /// outcome. <c>netsh</c> answers that with a non-zero code and a localised sentence - which is
    /// why the code is read and the sentence is not.
    /// </returns>
    internal static int Delete() =>
        Netsh($"advfirewall firewall delete rule name=\"{Name}\"");

    /// <summary>
    /// Adds the rule. Always as delete-then-add, never as <c>set rule</c>.
    /// <para>
    /// Windows allows several rules with the same display name, and a repeated <c>add</c>
    /// <b>creates duplicates instead of replacing</b> - exactly the leftovers this is about.
    /// <c>set rule</c> would be the obvious alternative and is worse: it fails when no rule is
    /// there yet, and with three duplicates present it changes all three rather than getting rid
    /// of two. Delete-then-add is the only form that lands on exactly one rule from every starting
    /// state (Part 9).
    /// </para>
    /// </summary>
    internal static int Add() =>
        Netsh(
            $"advfirewall firewall add rule name=\"{Name}\" dir=in action=allow "
            + $"program=\"{Path}\" enable=yes profile={Profiles}");

    /// <summary>
    /// Says what happened, in a form somebody can read in a console window that closes itself.
    /// Written to the standard output rather than a log: this process lives for a second and has
    /// no data root of its own.
    /// </summary>
    internal static void Report(string what, int exitCode) =>
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{what}: rule \"{Name}\" -> {Path} (netsh exit {exitCode})"));

    private static int Netsh(string arguments)
    {
        using var netsh = Process.Start(new ProcessStartInfo("netsh", arguments)
        {
            // No window and no shell: this already runs elevated, and a console flashing up in
            // front of the DM would be the only thing he saw of it.
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (netsh is null)
        {
            return -1;
        }

        netsh.WaitForExit();

        return netsh.ExitCode;
    }
}
