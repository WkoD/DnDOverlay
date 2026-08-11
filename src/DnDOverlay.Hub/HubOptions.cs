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
}
