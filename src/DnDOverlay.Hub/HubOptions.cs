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
}
