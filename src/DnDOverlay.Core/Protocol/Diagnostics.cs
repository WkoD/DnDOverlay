namespace DnDOverlay.Core.Protocol;

/// <summary>
/// Switches a device's diagnostics stream on or off (Part 4). Sent while a tile of that device is on
/// its diagnostic view; the stream ends with the connection by itself.
/// </summary>
public sealed record DiagnosticsRequestMessage(bool On) : ProtocolMessage;

/// <summary>
/// What a display measures about itself, about once a second, for as long as it was asked to
/// (Part 4).
/// <para>
/// <b>Requested rather than sent.</b> Four devices with two screens each sending telemetry all
/// evening would be rank-4 traffic without a reader. And <b>transient with a slot of one</b>, for
/// the same reason as the touch points: a frame time from a moment ago is not inaccurate, it is
/// worthless. That is also why it is not on the <c>Pong</c>, which is a sign of life and is never
/// dropped.
/// </para>
/// <para>
/// The bar at the device needs none of this - it renders from its own numbers. This is only the way
/// into the control, so the diagnostic view shows the same numbers as the bar: one source, two
/// renderings (Part 1).
/// </para>
/// </summary>
public sealed record DiagnosticsMessage(DiagnosticsReading Reading) : ProtocolMessage, IReplacing<ProtocolMessage>
{
    /// <summary>One slot: a connection belongs to one device, and the device has one reading.</summary>
    public string Slot => "diagnostics";

    /// <summary>The newer reading says everything the older one did.</summary>
    public ProtocolMessage Over(ProtocolMessage waiting, int gapMs) => this;

    /// <summary>A reading carries no ages, so a wait moves nothing in it.</summary>
    public ProtocolMessage? Sent(int waitedMs) => this;
}

/// <summary>
/// One reading of the diagnostics stream. The frame numbers come from the same counter as the
/// display's log line and its bar, rounded once at the counter.
/// </summary>
/// <param name="Frames">
/// How many frames the thirty seconds are made of - what the three numbers after it are worth. Zero
/// while there are too few to say anything; then the three are <see langword="null"/>.
/// </param>
/// <param name="CpuPercent">The process's share of the whole machine since the last reading.</param>
/// <param name="Downloading">Pictures being fetched, checked or decoded right now.</param>
/// <param name="Waiting">Pictures on the list that have not started.</param>
/// <param name="CachedPictures">Pictures in the store on disk.</param>
/// <param name="CacheBytes">What the store on disk occupies.</param>
/// <param name="MemoryBytes">Committed memory of the process - the number the memory cap is held against.</param>
/// <param name="MemoryCapBytes">The cap itself, so the number above can be read (Part 6).</param>
public sealed record DiagnosticsReading(
    int Frames,
    double? MedianMs,
    double? P95Ms,
    double? MaxMs,
    double CpuPercent,
    int Downloading,
    int Waiting,
    int CachedPictures,
    long CacheBytes,
    long MemoryBytes,
    long MemoryCapBytes);
