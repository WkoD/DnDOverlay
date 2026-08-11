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

    /// <summary>How many devices are connected right now - the ceiling is checked against this.</summary>
    public int Count => _connections.Count;

    public void Add(DisplayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _connections[connection.Device] = connection;
    }

    public bool TryGet(DeviceId device, out DisplayConnection connection) =>
        _connections.TryGetValue(device, out connection!);

    /// <summary>
    /// Removes this connection, and only this one.
    /// <para>
    /// Removing by device would be the obvious shape and would take the WRONG connection out on
    /// every fast reconnect: the new one has already registered under the same
    /// <see cref="DeviceId"/> when the old one finishes tidying up. The symptom would be a
    /// display that vanishes from the list moments after it came back.
    /// </para>
    /// </summary>
    public void Remove(DisplayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _ = ((ICollection<KeyValuePair<DeviceId, DisplayConnection>>)_connections)
            .Remove(new KeyValuePair<DeviceId, DisplayConnection>(connection.Device, connection));
    }

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
