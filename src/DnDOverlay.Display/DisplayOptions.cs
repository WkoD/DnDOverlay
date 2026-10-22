using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Display;

/// <summary>
/// What the command line can say. Everything here is a START argument and never a stored value -
/// the real configuration lives in display.json from M1b (Part 6).
/// </summary>
/// <param name="Host">
/// Null when <c>--host</c> was not given. Null rather than "localhost", because "not said" and
/// "said localhost" have to stay distinguishable: the stored host from display.json fills the
/// gap, and a default would silently overrule it.
/// </param>
/// <param name="DataRoot">
/// <c>--data</c>, or null for the installed location. A development switch and nothing else: it
/// appears in no installer and in no file, and it cannot be set remotely - it decides where the
/// configuration lies, so it could not live in the configuration (Part 9).
/// </param>
/// <param name="After">
/// <c>--after</c>: the process this one replaces, which it waits out before it touches anything -
/// set by a restart from the control and by nobody else (see <see cref="ForRestart"/>).
/// </param>
internal sealed record DisplayOptions(
    string? Host,
    int Port,
    string? DeviceName,
    bool Windowed,
    string? DataRoot,
    int? After = null)
{
    private const string AfterSwitch = "--after";
    private const string NameSwitch = "--name";

    /// <summary>The switches that take the next argument as their value - see <see cref="Parse"/>.</summary>
    private static readonly string[] ValueSwitches = ["--host", "--port", NameSwitch, "--data", AfterSwitch];

    /// <summary>
    /// The arguments a restart from the control starts the new process with: this process's own,
    /// and the switch that makes the new one wait until this one is gone.
    /// <para>
    /// <b>The new process is started first and waits</b>, rather than started last: a start that
    /// fails - the executable locked mid-update - leaves this one running and connected instead of
    /// gone. The wait is what keeps the two apart: until the old process ends, its socket still
    /// answers the hub's pings, so a Hello from the new one within that time would be taken for a
    /// clone and put before the DM; and the old one empties the picture store on its way out while
    /// the new one would already be filling it (Part 5, Part 7).
    /// </para>
    /// <para>
    /// An <c>--after</c> from an earlier restart is replaced, not carried along. So is the value of a
    /// <c>--name</c>: it carries the name the device has NOW (<paramref name="deviceName"/>). Carried
    /// along, the start argument would beat a rename from the control and give the device its old name
    /// back with the restart the DM sent (DM decision of 22.10.2026). Without a <c>--name</c> nothing
    /// is added - a rename is in display.json, which fills the gap.
    /// </para>
    /// </summary>
    internal static List<string> ForRestart(IReadOnlyList<string> args, int predecessor, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(deviceName);

        var kept = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == AfterSwitch && i + 1 < args.Count && int.TryParse(args[i + 1], out _))
            {
                i++;
                continue;
            }

            if (args[i] == NameSwitch && i + 1 < args.Count)
            {
                kept.Add(NameSwitch);
                kept.Add(deviceName);
                i++;
                continue;
            }

            // A switch left without its value at the very end was passed over at this start; carried
            // along, it would take the wait added below as its value - a host or a data root called
            // "--after", and a new process that no longer waits for this one.
            if (i == args.Count - 1 && ValueSwitches.Contains(args[i]))
            {
                continue;
            }

            kept.Add(args[i]);
        }

        kept.Add(AfterSwitch);
        kept.Add(predecessor.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return kept;
    }

    /// <summary>
    /// What the three queues in front of this device's socket may hold. The same shape the hub
    /// uses, with the same numbers, because it is the same rule at the other end of one wire
    /// (Part 4).
    /// <para>
    /// It is a constant rather than a setting: the ceilings say when a counterpart has stopped
    /// taking anything, and that is not a matter of taste. The hub's are settable only because a
    /// test has to be able to reach them.
    /// </para>
    /// </summary>
    internal static SendLimits SendLimits { get; } =
        new(MaxStateMessages: 256, MaxStateBytes: 8 * 1024 * 1024, MaxTransientSlots: 8, TimeSpan.FromSeconds(10));

    internal static DisplayOptions Parse(IReadOnlyList<string> args)
    {
        string? host = null;
        var port = Protocol.DefaultPort;
        string? name = null;
        var windowed = false;
        string? dataRoot = null;
        int? after = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                // Sets every screen to windowed presentation FOR THIS SESSION, storing nothing.
                // The windowed mode itself is a display parameter per screen (Part 6); this
                // switch is only the quick grip to it.
                case "--windowed":
                    windowed = true;
                    break;

                case "--host" when i + 1 < args.Count:
                    host = args[++i];
                    break;

                case "--port" when i + 1 < args.Count && int.TryParse(args[i + 1], out var parsed):
                    port = parsed;
                    i++;
                    break;

                case NameSwitch when i + 1 < args.Count:
                    name = args[++i];
                    break;

                case "--data" when i + 1 < args.Count:
                    dataRoot = args[++i];
                    break;

                case AfterSwitch when i + 1 < args.Count && int.TryParse(args[i + 1], out var predecessor):
                    after = predecessor;
                    i++;
                    break;

                default:
                    break;
            }
        }

        return new DisplayOptions(host, port, name, windowed, dataRoot, after);
    }
}
