using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// The operations range, 4000–4999: what the process does to itself - where it stores things,
/// which port it took, that it is going away. It is the fourth range beside connection (1000),
/// assets (2000) and display (3000), and it exists because a data root is none of those three.
/// Numbers are global, strictly ascending within their range and never reused (Part 8).
/// </summary>
internal static partial class ControlLog
{
    /// <summary>
    /// Said out loud on purpose: a development run and an installed copy differ in exactly this
    /// one path, and a run that quietly used the wrong root would be indistinguishable from a
    /// correct one until it had already touched the DM's own campaigns (Part 9).
    /// </summary>
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Data root: {Path}")]
    internal static partial void DataRootChosen(ILogger logger, string path);
}
