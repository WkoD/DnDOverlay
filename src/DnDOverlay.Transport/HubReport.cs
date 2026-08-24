using System.Net.WebSockets;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Transport;

/// <summary>
/// What the display says when its own socket goes wrong. The queues are in Core and serve both
/// ends (<see cref="SendQueues"/>); only the event identifiers are per process, because the hub
/// names the address at the far end and this end names the hub (Part 8).
/// </summary>
internal sealed class HubReport(ILogger logger, Uri hubUri) : ISendReport
{
    public void StateQueueFull(int queued, long bytes) =>
        TransportLog.SendQueueFull(logger, hubUri, queued, bytes);

    public void WriteTimedOut(TimeSpan limit) => TransportLog.SendTimedOut(logger, hubUri, limit);

    /// <summary>
    /// Said nowhere, and that is the same answer the send loop gave before these queues existed:
    /// a socket that refuses a write has ended, and the receive side reports the end. Two lines
    /// for one event would be noise at exactly the moment the log is being read.
    /// </summary>
    public void SendFailed(WebSocketException exception)
    {
    }
}
