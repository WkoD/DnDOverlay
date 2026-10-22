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
    private readonly Lock _admission = new();
    private bool _released;

    /// <summary>How many devices are connected right now - the ceiling is checked against this.</summary>
    public int Count => _connections.Count;

    /// <summary>
    /// Fires when somebody arrived or left. It is what turns a device in the tree from connected to
    /// not, and it is the only source for that - the screen catalogue keeps a departed device's
    /// screens on purpose and therefore cannot say it (Part 3).
    /// </summary>
    public event Action? Changed;

    /// <summary>Everyone who is here right now.</summary>
    public IReadOnlyCollection<DisplayConnection> All => _connections.Values.ToList();

    public void Add(DisplayConnection connection) => _ = TryAdd(connection);

    /// <summary>
    /// Registers a connection - unless the screens have been released (<see cref="Release"/>), in
    /// which case nobody is let in any more and the caller says goodbye instead.
    /// <para>
    /// Under the same lock as the release, and that is the point: a connection that was being set up
    /// while the goodbye went round is either in the release's list or refused here - there is no
    /// third case in which it slips through and lights the table up again.
    /// </para>
    /// </summary>
    public bool TryAdd(DisplayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        lock (_admission)
        {
            if (_released)
            {
                return false;
            }

            _connections[connection.Device] = connection;
        }

        Changed?.Invoke();

        return true;
    }

    /// <summary>Whether the screens have been released - the control is on its way out.</summary>
    public bool Released
    {
        get
        {
            lock (_admission)
            {
                return _released;
            }
        }
    }

    /// <summary>
    /// "Release the screens" (Part 4, Part 7): hands back everyone who is here, and from now on lets
    /// nobody in. Without the second half a display that took its <c>GoIdle</c> quickly came straight
    /// back - a welcomed connection that ended is worth a reconnect after a second - while the hub was
    /// still waiting for a slower one, and was served its wishes and the evening's scene again. The
    /// rescue mark and the takeover window followed a minute after the control was gone.
    /// </summary>
    public IReadOnlyList<DisplayConnection> Release()
    {
        lock (_admission)
        {
            _released = true;

            return [.. _connections.Values];
        }
    }

    /// <summary>
    /// Something about this connection that the device list shows has changed - its battery. Said
    /// only while it is the registered one: a connection already displaced by a reconnect has
    /// nothing left to show.
    /// </summary>
    public void Refresh(DisplayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (_connections.TryGetValue(connection.Device, out var current) && ReferenceEquals(current, connection))
        {
            Changed?.Invoke();
        }
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

        if (((ICollection<KeyValuePair<DeviceId, DisplayConnection>>)_connections)
            .Remove(new KeyValuePair<DeviceId, DisplayConnection>(connection.Device, connection)))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Sends a patch to every device it concerns, cut down to the operations for that device's
    /// screens. A device whose screens the patch never mentions gets nothing at all.
    /// </summary>
    /// <param name="except">
    /// A device that is not to be told, because it is the one that asked for this - conflict rule 2
    /// (Part 4): a display corrects itself on a broadcast "that it did not cause itself", and it
    /// cannot tell the difference from the outside, so the hub keeps the rule for it by not
    /// sending. Used for the middle of a gesture, where the hand knows better than any answer could
    /// and an answer that arrives late is a picture walking backwards through its own movement.
    /// <para>
    /// <b>It does not reach the surfaces.</b> The control's thumbnail follows a hand at the table
    /// through <c>SessionEvent.ScenePatched</c>, which is a separate channel, so leaving a device
    /// out here takes nothing away from anybody watching.
    /// </para>
    /// </param>
    public void Dispatch(ScenePatch patch, DeviceId? except = null)
    {
        ArgumentNullException.ThrowIfNull(patch);

        foreach (var connection in _connections.Values)
        {
            if (connection.Device == except)
            {
                continue;
            }

            var mine = patch.Ops.Where(op => connection.Screens.Contains(op.Screen)).ToList();

            if (mine.Count > 0)
            {
                connection.TrySend(new ScenePatchMessage(new ScenePatch(mine)));
            }
        }
    }
}
