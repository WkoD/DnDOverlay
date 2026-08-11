using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Display;

/// <summary>
/// What the command line can say. Everything here is a START argument and never a stored value -
/// the real configuration lives in display.json from M1b (Part 6).
/// </summary>
/// <param name="DataRoot">
/// <c>--data</c>, or null for the installed location. A development switch and nothing else: it
/// appears in no installer and in no file, and it cannot be set remotely - it decides where the
/// configuration lies, so it could not live in the configuration (Part 9).
/// </param>
internal sealed record DisplayOptions(
    string Host,
    int Port,
    string DeviceName,
    bool Windowed,
    string? DataRoot)
{
    internal static DisplayOptions Parse(IReadOnlyList<string> args)
    {
        var host = "localhost";
        var port = Protocol.DefaultPort;
        var name = Environment.MachineName;
        var windowed = false;
        string? dataRoot = null;

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

                case "--name" when i + 1 < args.Count:
                    name = args[++i];
                    break;

                case "--data" when i + 1 < args.Count:
                    dataRoot = args[++i];
                    break;

                default:
                    break;
            }
        }

        return new DisplayOptions(host, port, name, windowed, dataRoot);
    }
}
