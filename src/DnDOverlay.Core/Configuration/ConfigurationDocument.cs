namespace DnDOverlay.Core.Configuration;

/// <summary>
/// What every stored configuration document carries: the version of its cluster.
/// </summary>
public interface IConfigurationDocument
{
    /// <summary>
    /// The schema version of the CLUSTER this file belongs to, not of this file (rule 6).
    /// </summary>
    int SchemaVersion { get; }
}

/// <summary>
/// One version number for the whole configuration cluster - control.json and display.json.
/// <para>
/// Clustering is the point: there is ONE moment at which a migration happens, instead of a file
/// from two years ago triggering a migration path in the middle of a session (rule 6). The price
/// is named and accepted: a change to control.json also raises the number of display.json, and
/// its read path then has nothing to do but raise the number. An empty migration step is
/// allowed, and cheaper than what clustering prevents.
/// </para>
/// </summary>
public static class ConfigurationSchema
{
    /// <summary>The version this build writes.</summary>
    public const int Version = 1;
}

/// <summary>What reading a configuration file did.</summary>
public enum ConfigurationOutcome
{
    /// <summary>The file was there and readable.</summary>
    Loaded,

    /// <summary>There was no file. A fresh document was created - the ordinary first start.</summary>
    Created,

    /// <summary>
    /// The file was unreadable or written by a newer build. It was set aside under a new name
    /// and a fresh document created, as on a new installation.
    /// <para>
    /// This must never stop the application from starting. On a display PC the cost is its
    /// identity - DeviceId, token, screen names are gone and it reappears as a new device - and
    /// that is still far better than a machine that will not come up because of a broken text
    /// file, on a computer with no keyboard. A device that has to be renamed can be rescued;
    /// one that does not start cannot (Part 6).
    /// </para>
    /// </summary>
    Replaced,
}

/// <summary>The result of reading, with everything the caller needs in order to report it.</summary>
/// <param name="SetAside">Where the unreadable file went, or null.</param>
public sealed record ConfigurationLoad<T>(T Value, ConfigurationOutcome Outcome, string? SetAside)
    where T : class, IConfigurationDocument;
