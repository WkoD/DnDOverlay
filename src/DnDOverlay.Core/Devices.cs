namespace DnDOverlay.Core;

/// <summary>
/// One device as the control shows it: what it reported about itself, plus what only the control
/// knows - whether it is here right now, how far away it is, and how each of its screens stands.
/// <para>
/// It is the shape the two-stage tree in the device window is drawn from - device, and its screens
/// underneath. Flat would be unreadable with two devices of two monitors each, and a list of
/// screens alone could not say which monitor hangs off which machine (Part 7).
/// </para>
/// <para>
/// A device that is NOT connected stays in here with its screens. That is the point rather than a
/// nicety: its wishes and display parameters live in the control, and setting them before the
/// display PC is even switched on is what the device window exists for (Part 3, Part 7).
/// </para>
/// </summary>
/// <param name="Connected">
/// Whether a socket is open right now. The four fields after it are only true while it is, and are
/// <see langword="null"/> otherwise - a version or a round trip remembered from last Tuesday would
/// be a number that reads as current and is not.
/// </param>
/// <param name="Address">
/// What a human uses to tell two devices apart when the names do not help. Never used to identify
/// one: it changes with DHCP and with the dock, and two processes on one machine share the loopback
/// (Part 3).
/// </param>
public sealed record DeviceView(
    DeviceId Device,
    string Name,
    bool Connected,
    IReadOnlyList<ScreenView> Screens,
    string? Address = null,
    string? AppVersion = null,
    int? ProtocolVersion = null,
    TimeSpan? RoundTrip = null)
{
    /// <summary>
    /// Structural over the screen list, like every other list-bearing record here: a record
    /// compares list members by REFERENCE, so two views built from the same catalogue a moment
    /// apart would never compare equal - and the surface asking "has anything changed?" would
    /// redraw on every beat.
    /// </summary>
    public bool Equals(DeviceView? other) =>
        other is not null
        && Device == other.Device
        && Name == other.Name
        && Connected == other.Connected
        && Address == other.Address
        && AppVersion == other.AppVersion
        && ProtocolVersion == other.ProtocolVersion
        && RoundTrip == other.RoundTrip
        && Screens.SequenceEqual(other.Screens);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Device);
        hash.Add(Name);
        hash.Add(Connected);
        hash.Add(Address);
        hash.Add(AppVersion);
        hash.Add(ProtocolVersion);
        hash.Add(RoundTrip);

        foreach (var screen in Screens)
        {
            hash.Add(screen);
        }

        return hash.ToHashCode();
    }
}
