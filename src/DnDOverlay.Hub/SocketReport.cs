using System.Net.WebSockets;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Hub;

/// <summary>
/// What the hub says when a socket of its own goes wrong. The queues themselves are in Core and
/// serve both ends of the wire (<see cref="SendQueues"/>); the event identifiers are not shared,
/// because the hub names the address at the far end and the display names the hub (Part 8).
/// </summary>
/// <param name="address">
/// Who is at the other end. The address rather than the device, because these queues exist from
/// the moment the socket is accepted - before any <c>Hello</c> has said which device this is. It
/// is also what a person uses to tell two connections apart while setting up (Part 3).
/// </param>
internal sealed class SocketReport(string address, ILogger logger) : ISendReport
{
    public void StateQueueFull(int queued, long bytes) =>
        HubLog.StateQueueFull(logger, address, queued, bytes);

    public void WriteTimedOut(TimeSpan limit) => HubLog.WriteTimedOut(logger, address, limit);

    public void SendFailed(WebSocketException exception) => HubLog.SendFailed(logger, exception, address);
}
