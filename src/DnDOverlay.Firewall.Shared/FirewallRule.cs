using System.Diagnostics;
using System.Globalization;

namespace DnDOverlay.Firewall;

/// <summary>
/// What the three helpers share: which rule they are talking about, and how a <c>netsh</c> call is
/// made.
/// <para>
/// It is a linked source file rather than a library on purpose. Each helper is meant to be one
/// self-contained executable whose <b>name</b> is the whole message the UAC prompt carries; a
/// second assembly beside it would buy nothing and would have to be deployed with it.
/// </para>
/// <para>
/// <b>What differs between the helpers is one constant</b> - the profiles they write - and nothing
/// else. That is what keeps "no arguments" true while still offering the DM a choice: the choice is
/// made by picking WHICH executable to elevate, so the prompt still names the act (Part 9).
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
    /// Home and domain, the profiles a rule always carries. Public is never in this set; it is
    /// added by the one helper whose name says so.
    /// </summary>
    internal const string HomeProfiles = "private,domain";

    /// <summary>
    /// The same plus public - written only by <c>FirewallAddAnywhere</c>, and only after the DM has
    /// read what it costs. Private and domain stay in it, so coming home needs no second press.
    /// </summary>
    internal const string EveryProfile = "private,domain,public";

    /// <summary>
    /// A rule is PROGRAM-based, never port-based, because the port is configurable: a rule nailed
    /// to 47800 would be worthless after the first port change (Part 9).
    /// </summary>
    internal static string Path => PathIn(Directory);

    /// <summary>
    /// Two names, and the difference is not cosmetic. Without it every development run would leave
    /// a rule behind pointing at a path that no longer exists, and half a year later nobody could
    /// say which of the three rules is the live one.
    /// </summary>
    internal static string Name => NameFor(Directory);

    private static string Directory =>
        System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>
    /// The naming rule, as a function of the directory rather than of the running process - which
    /// is the only form a test can reach. The helpers themselves never pass anything in; they take
    /// their own location, and the decision stays inside the elevated program (Part 9).
    /// </summary>
    internal static string NameFor(string directory) =>
        Installed(directory) ? "DnDOverlay Control" : "DnDOverlay Control (dev)";

    /// <summary>
    /// Whether this copy sits where the installer puts it. Decided HERE and never by the caller: a
    /// program that starts elevated and lets an unprivileged caller tell it <b>what</b> to do is a
    /// privilege escalation with a covering letter.
    /// </summary>
    internal static bool Installed(string directory) =>
        directory.StartsWith(
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "DnDOverlay"),
            StringComparison.OrdinalIgnoreCase);

    internal static string PathIn(string directory) =>
        System.IO.Path.Combine(directory, Program);

    /// <summary>
    /// Removes every inbound rule that concerns this program - <b>by path, not by name</b>.
    /// <para>
    /// By name was not enough and quietly so: Windows writes rules of its own, named after the
    /// executable, when the "allow access?" box is answered - and a BLOCK rule when it is dismissed
    /// (measured). Those are invisible to a search by our name, they beat every allow beside them,
    /// and they are the reason a machine can sit there with our rule in place and nothing getting
    /// through. A name is also the wrong key in the other direction: it points at whatever somebody
    /// called a rule, while the path points at what actually gets let in.
    /// </para>
    /// <para>
    /// The consequence is stated in the button that calls this: <b>everything</b> for this path
    /// goes, hand-made rules included. That is the honest reading of "remove", and the view lists
    /// beforehand what will be taken.
    /// </para>
    /// </summary>
    /// <returns>
    /// The exit code, swallowed by the caller where "there was nothing to remove" is a fine
    /// outcome. <c>netsh</c> answers that with a non-zero code and a localised sentence - which is
    /// why the code is read and the sentence is not.
    /// </returns>
    internal static int Delete() =>
        Netsh($"advfirewall firewall delete rule name=all dir=in program=\"{Path}\"");

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
    internal static int Add(string profiles) =>
        Netsh(
            $"advfirewall firewall add rule name=\"{Name}\" dir=in action=allow "
            + $"program=\"{Path}\" enable=yes profile={profiles}");

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
