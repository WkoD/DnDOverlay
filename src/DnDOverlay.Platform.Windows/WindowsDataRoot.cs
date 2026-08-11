using DnDOverlay.Core;

namespace DnDOverlay.Platform.Windows;

/// <summary>
/// Where the data root lies when nobody says otherwise.
/// <para>
/// "Fixed" means the DM cannot change it - not that it is hardcoded somewhere. The path is
/// handed in (rule 10), and this is the one line that asks the operating system for it; none of
/// the five libraries knows it, and the architecture test enforces that by forbidding
/// <c>Environment.GetFolderPath</c> there (Part 9, Part 11).
/// </para>
/// </summary>
public static class WindowsDataRoot
{
    /// <summary>
    /// <c>%LOCALAPPDATA%\DnDOverlay</c>.
    /// <para>
    /// Deliberately not <c>%APPDATA%</c>: that roams. On a domain profile two display PCs under
    /// the same account would carry the SAME DeviceId, the clone detection would turn one away,
    /// and the fault would be hunted in the network rather than in the user profile (Part 6).
    /// </para>
    /// </summary>
    public static DataRoot Default => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DnDOverlay"));

    /// <summary>
    /// The root to use for this run: <paramref name="fromCommandLine"/> if <c>--data</c> was
    /// given, otherwise <see cref="Default"/>. A relative path is resolved against the working
    /// directory, which is what makes <c>--data dev-data</c> land inside the project (Part 9).
    /// </summary>
    public static DataRoot Resolve(string? fromCommandLine) =>
        string.IsNullOrWhiteSpace(fromCommandLine)
            ? Default
            : new DataRoot(System.IO.Path.GetFullPath(fromCommandLine));
}
