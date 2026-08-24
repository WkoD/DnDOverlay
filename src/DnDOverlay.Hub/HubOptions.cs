using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Hub;

/// <summary>What the hub needs to know about itself.</summary>
public sealed class HubOptions
{
    /// <summary>
    /// Identifies THIS control, so a display stays bound to the one it was paired with. The
    /// address will not do for that - it changes, and a second control on the same network is no
    /// invention (Part 4).
    /// <para>
    /// From M1b this is created once and kept in control.json. In M1a it lives for the run.
    /// </para>
    /// </summary>
    public Guid ControlId { get; set; } = Guid.NewGuid();

    /// <summary>The TCP port Kestrel binds, on all interfaces.</summary>
    public int Port { get; set; } = Protocol.DefaultPort;

    /// <summary>
    /// What was paired before, as a snapshot at construction. The control reads
    /// <c>control.json</c>, decrypts the tokens and hands them over; the hub never learns where
    /// the file is (Part 7).
    /// </summary>
    public IReadOnlyList<PairedDevice> KnownDevices { get; set; } = [];

    /// <summary>
    /// Every screen that was ever reported, with the DM's wish and its display parameters - again
    /// a snapshot at construction, from the same file.
    /// <para>
    /// It is what makes the hub able to place, cap and rotate for a screen whose device is
    /// switched off, which is the whole of "a screen is fully playable in every state"
    /// (Part 3, Part 7).
    /// </para>
    /// </summary>
    public IReadOnlyList<Core.Configuration.KnownScreen> KnownScreens { get; set; } = [];

    /// <summary>
    /// "Do not accept new devices" (Part 4). Off, requests are only logged - the answer to a
    /// stranger who keeps knocking, without a dialog in front of the group.
    /// </summary>
    public bool AcceptNewDevices { get; set; } = true;

    /// <summary>
    /// Every limit here is measured so that ordinary use never reaches it - an order of magnitude
    /// above the realistic maximum (Part 4). The reflex goes the other way, because tight numbers
    /// feel safer; at a games table that is wrong. <b>A false alarm costs more than the attack the
    /// limit guards against</b>: it interrupts the session in front of the group, looks like a
    /// defect, and nobody finds the cause in a number somebody picked a year ago.
    /// </summary>
    public int MaxConnections { get; set; } = 64;

    /// <summary>Realistically one to four devices, and only while setting up.</summary>
    public int MaxOpenPairingRequests { get; set; } = 8;

    /// <summary>Realistically one per device, once. Twenty leaves room for a device that retries.</summary>
    public int MaxPairingAttemptsPerAddressPerMinute { get; set; } = 20;

    /// <summary>
    /// How long the connection that is already there gets to answer the probe before it counts as
    /// dead. A crashed display coming straight back and a clone look identical; one second of
    /// silence tells them apart, and it is an ANSWER that decides rather than a deadline
    /// (Part 4).
    /// </summary>
    public TimeSpan CloneProbe { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The state queue, by count. Generous enough for a scene with many items arriving in one go;
    /// beyond that the counterpart is demonstrably taking nothing off the socket (Part 4).
    /// </summary>
    public int MaxStateMessages { get; set; } = 256;

    /// <summary>
    /// And by bytes, because one <c>SceneSnapshot</c> with twenty items weighs as much as a
    /// hundred small messages. Either ceiling ends the connection on its own.
    /// </summary>
    public long MaxStateBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// The transient tier, by how many KINDS may wait at once. Part 4 gives <c>TouchPoints</c>,
    /// <c>Diagnostics</c> and <c>WindowList</c> a replacing slot of one each, and touch points get
    /// one per screen - so the ceiling is the number of slots this build can open rather than a
    /// queue length.
    /// <para>
    /// <b>This replaces the floor that stood here until M3c</b>: one small queue dropping its
    /// oldest, which the option's own comment called the floor under the rule rather than a
    /// substitute for it. The messages exist now, so the rule from Part 4 applies.
    /// </para>
    /// </summary>
    public int MaxTransientSlots { get; set; } = 8;

    /// <summary>
    /// How long one write may take before the counterpart counts as gone. Longer than any Wi-Fi
    /// dropout worth keeping a connection for, shorter than a DM's patience (Part 4).
    /// <para>
    /// It exists because a peer that holds the connection open and accepts nothing would otherwise
    /// only be noticed once the queue had filled - late, and after the memory had already been
    /// spent.
    /// </para>
    /// </summary>
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>What the three queues in front of one socket are given, gathered from the four above.</summary>
    public SendLimits SendLimits =>
        new(MaxStateMessages, MaxStateBytes, MaxTransientSlots, WriteTimeout);

    /// <summary>The heartbeat's beat, so the connection indicator is right without a noticeable lag.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the other end may say nothing at all before the connection counts as dead. The
    /// ceiling for a Wi-Fi dropout that should <b>not</b> be treated as a disconnection (Part 4).
    /// </summary>
    public TimeSpan SilenceBeforeDead { get; set; } = TimeSpan.FromSeconds(12);
}
