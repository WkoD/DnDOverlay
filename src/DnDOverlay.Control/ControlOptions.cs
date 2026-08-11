namespace DnDOverlay.Control;

/// <summary>
/// What the command line can say. Everything here is a START argument and never a stored value -
/// the configuration lives in control.json (Part 7).
/// </summary>
/// <param name="DataRoot">
/// <c>--data</c>, or null for the installed location. The only way to move the five places at
/// once, and a development switch alone: it decides where the configuration lies, so it could
/// not itself live in the configuration (Part 9).
/// </param>
internal sealed record ControlOptions(string? DataRoot)
{
    internal static ControlOptions Parse(IReadOnlyList<string> args)
    {
        string? dataRoot = null;

        for (var i = 0; i < args.Count; i++)
        {
            // --windowed is a display switch and is accepted here without effect, so that one
            // compound configuration can hand the same arguments to both processes (Part 2).
            if (args[i] is "--data" && i + 1 < args.Count)
            {
                dataRoot = args[++i];
            }
        }

        return new ControlOptions(dataRoot);
    }
}
