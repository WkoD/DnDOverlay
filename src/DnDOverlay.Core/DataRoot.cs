namespace DnDOverlay.Core;

/// <summary>
/// Where everything durable is written, as one value that is handed in (rule 10).
/// <para>
/// The type names the five places and composes nothing but strings; the VALUE comes from the
/// application, because only it may ask the operating system where <c>%LOCALAPPDATA%</c> is.
/// That split is what makes <c>--data</c> work at all: a development run moves the whole root
/// into the project and must not touch a single file of the installed copy on the same machine
/// (Part 9), and the acceptance step for that promise is only as reliable as the answer to
/// "what is everything" - so the answer lives in exactly one place.
/// </para>
/// </summary>
/// <param name="Path">The root directory. Nothing here creates it.</param>
public readonly record struct DataRoot(string Path)
{
    /// <summary>The DM side: known devices, tokens, screen wishes, view state (Part 7).</summary>
    public string ControlConfiguration => In("control.json");

    /// <summary>What a display PC knows about itself (Part 6).</summary>
    public string DisplayConfiguration => In("display.json");

    /// <summary>
    /// The campaign folder as SHIPPED, not as it is in force.
    /// <para>
    /// It is the only one of the five the DM can relocate, and the effective path is stored in
    /// <c>control.json</c> (Part 3). The name says so: whoever looks here for the path in force
    /// finds nothing and has to ask, which is the right outcome. Leaving it out would not help -
    /// the derivation would still have to live somewhere, just without its four siblings.
    /// </para>
    /// </summary>
    public string CampaignsDefault => In("campaigns");

    /// <summary>
    /// The display's image store. Fixed on purpose: it is transient and governed by its size
    /// limit, not by its location (Part 9).
    /// </summary>
    public string Cache => In("cache");

    /// <summary>Rolling file log of either application (Part 8).</summary>
    public string Logs => In("logs");

    // System.IO.Path is shadowed by the Path property of this record, hence the full name.
    private string In(string name) => System.IO.Path.Combine(Path, name);
}
