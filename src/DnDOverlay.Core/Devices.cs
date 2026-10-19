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
/// Whether a socket is open right now. The five fields after it are only true while it is, and are
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
    TimeSpan? RoundTrip = null,
    BatteryInfo? Battery = null)
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
        && Battery == other.Battery
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
        hash.Add(Battery);

        foreach (var screen in Screens)
        {
            hash.Add(screen);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// How full a display's battery is, and whether it is on the cable.
/// </summary>
/// <param name="Percent">0 to 100.</param>
/// <param name="PluggedIn">
/// Whether it hangs on the cable, as Windows shows it with its lightning sign. Without it 18 % would
/// be an alarm while the device is charging - "18 % and falling" is a different message from
/// "18 % and rising" (Part 7).
/// </param>
public sealed record BatteryInfo(int Percent, bool PluggedIn)
{
    /// <summary>
    /// From here down the fill is red - the last confirmation, not the first message: the DM sees
    /// it fall from 60 to 40 to 20 and acts when it suits him (Part 7). It is also where the control
    /// warns about its own battery, the one real warning (DM, 19.10.2026).
    /// </summary>
    public const int LowPercent = 10;

    /// <summary>Whether this reads as low - at or under <see cref="LowPercent"/>, on the cable or not.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Low => Percent <= LowPercent;
}
