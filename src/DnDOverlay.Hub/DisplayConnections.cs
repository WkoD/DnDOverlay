using System.Collections.Concurrent;
using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Hub;

/// <summary>
/// Who is currently listening, and the one place that decides what each of them gets.
/// <para>
/// The hub addresses PER CONNECTION. Broadcasting everything and letting the receiver sort it
/// out would be the simpler send path and the worse choice: every device would learn the scenes
/// of foreign tables, and the load would grow with the number of devices instead of with what is
/// happening (Part 4).
/// </para>
/// </summary>
public sealed class DisplayConnections
{
    private readonly ConcurrentDictionary<DeviceId, DisplayConnection> _connections = new();

    public void Add(DisplayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _connections[connection.Device] = connection;
    }

    public void Remove(DeviceId device) => _connections.TryRemove(device, out _);

    /// <summary>
    /// Sends a patch to every device it concerns, cut down to the operations for that device's
    /// screens. A device whose screens the patch never mentions gets nothing at all.
    /// </summary>
    public void Dispatch(ScenePatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        foreach (var connection in _connections.Values)
        {
            var mine = patch.Ops.Where(op => connection.Screens.Contains(op.Screen)).ToList();

            if (mine.Count > 0)
            {
                connection.TrySend(new ScenePatchMessage(new ScenePatch(mine)));
            }
        }
    }
}
