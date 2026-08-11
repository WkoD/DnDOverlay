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

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Information,
        Message = "No control.json yet - created one, control {ControlId}.")]
    internal static partial void ConfigurationCreated(ILogger logger, Guid controlId);

    /// <summary>
    /// Loud on purpose. control.json holds the known devices and their tokens, so a replacement
    /// means every display has to be paired again - and hearing that beats discovering it at
    /// the table (Part 6).
    /// </summary>
    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Warning,
        Message = "control.json was unreadable. Set aside as {SetAside}; starting with defaults, "
                  + "so paired devices have to be allowed again.")]
    internal static partial void ConfigurationReplaced(ILogger logger, string setAside);

    /// <summary>
    /// How much of the pairing survived the start. A dropped token means the profile changed -
    /// restored backup, reinstalled Windows, copied installation - and those devices simply pair
    /// again; the line exists so that this is read once instead of guessed at the table
    /// (Part 4).
    /// </summary>
    [LoggerMessage(
        EventId = 4007,
        Level = LogLevel.Information,
        Message = "{Restored} paired device(s) restored, {Dropped} dropped because the token did not decrypt.")]
    internal static partial void KnownDevicesRestored(ILogger logger, int restored, int dropped);
}
