using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub;

/// <summary>A device the DM has allowed, as the hub holds it: with its token in the clear.</summary>
/// <remarks>
/// The hub never sees <c>control.json</c>. It gets the known devices as a SNAPSHOT when it is
/// built and learns about later ones through <see cref="ISessionApi.ApprovePairingAsync"/> - the
/// control decrypts, the control writes, the control tells. Two writers on one debounced file
/// would be one too many, and which half won would be decided by the accident of the moment
/// (Part 7).
/// </remarks>
public sealed record PairedDevice(DeviceId Device, string Name, PairingRole Role, string Token);

/// <summary>What the DM sees while a device is waiting, and what he compares with the table.</summary>
/// <param name="BroughtUnknownToken">
/// This device presented a token this control does not know - almost always its own display after
/// the control lost <c>control.json</c>. It is a REQUEST rather than a rejection, because the
/// rejection led nowhere: the way out it pointed at needs a hand at a machine that has no keyboard
/// (Part 4). The gate is unchanged - nobody gets in without the DM either way; what changed is
/// whether he is offered the decision at all. Said in the row, because "knows this control" and
/// "brand new" are different things to be looking at.
/// </param>
public sealed record PendingPairing(
    DeviceId Device,
    string Name,
    string PairingCode,
    string Address,
    DateTimeOffset FirstSeen,
    bool IsClone,
    bool BroughtUnknownToken = false);

/// <summary>
/// A device that was turned away, kept with its reason.
/// <para>
/// A <c>Rejected</c> ends the connection, so without this the device would vanish from the list
/// although it is running and has a problem. It carries "last seen" rather than "reachable":
/// whether it is up right now is something the control cannot know once the socket is closed -
/// what it knows is when somebody last knocked (Part 4, Part 7).
/// </para>
/// </summary>
public sealed record RefusedDevice(
    DeviceId Device,
    string Name,
    RejectionReason Reason,
    DateTimeOffset LastSeen);

/// <summary>What the DM decided about a waiting device.</summary>
public abstract record PairingDecision
{
    /// <summary>Let in, with the token the control has already written to disk.</summary>
    public sealed record Approved(PairedDevice Device) : PairingDecision;

    /// <summary>Turned away, with the reason the device is told.</summary>
    public sealed record Refused(RejectionReason Reason) : PairingDecision;
}

/// <summary>
/// One waiting request. The connection holds it and awaits <see cref="Decision"/> - there is no
/// timer anywhere, which is what makes "an open request has no deadline, it has a connection"
/// true rather than intended (Part 4).
/// </summary>
public sealed class PendingRequest
{
    private readonly TaskCompletionSource<PairingDecision> _decision =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal PendingRequest(PendingPairing snapshot) => Snapshot = snapshot;

    /// <summary>What the device list shows.</summary>
    public PendingPairing Snapshot { get; internal set; }

    /// <summary>Completes when the DM decides. Never on its own.</summary>
    public Task<PairingDecision> Decision => _decision.Task;

    internal bool Settle(PairingDecision decision) => _decision.TrySetResult(decision);
}

/// <summary>The four ways a <c>Hello</c> can end.</summary>
public abstract record Admission
{
    /// <summary>A valid token: in without asking, the normal case at every power-on.</summary>
    public sealed record Admitted(PairedDevice Device) : Admission;

    /// <summary>
    /// Unknown device: the request is with the DM, and the connection waits.
    /// </summary>
    /// <param name="IsNew">
    /// Whether this opened the request or only refreshed one that was already standing. The log
    /// line hangs on it: a request is written once per pairing code, not once per connection, or
    /// an unpaired device on weak Wi-Fi fills the file by itself (Part 4).
    /// </param>
    public sealed record Waiting(PendingRequest Request, bool IsNew) : Admission;

    /// <summary>Turned away here and now, with a reason the device can act on.</summary>
    public sealed record Refused(RejectionReason Reason) : Admission;
}

/// <summary>
/// The pairing state machine from Part 4: <b>unknown - waiting - paired - rejected</b>, the four
/// inputs a <c>Hello</c> can carry, and the decisions that lead out again.
/// <para>
/// It is deliberately free of sockets and of files. What it needs from the outside is the clock
/// (rule 10) and the snapshot of what was paired before - everything else it decides from what a
/// <c>Hello</c> says.
/// </para>
/// </summary>
public sealed class PairingDirectory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<DeviceId, PairedDevice> _paired = [];
    private readonly Dictionary<DeviceId, PendingRequest> _pending = [];
    private readonly Dictionary<DeviceId, RefusedDevice> _refused = [];
    private readonly Dictionary<string, Attempts> _attempts = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly HubOptions _options;

    public PairingDirectory(IOptions<HubOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _time = time;
        AcceptNewDevices = _options.AcceptNewDevices;

        foreach (var device in _options.KnownDevices)
        {
            _paired[device.Device] = device;
        }
    }

    /// <summary>
    /// Fires when either list the device window shows has moved - somebody knocked, somebody was
    /// decided about, a rejection was taken back.
    /// <para>
    /// Always raised outside the lock. A subscriber reads both lists straight back, and reading
    /// them from inside would mean waiting on the lock that is announcing the change.
    /// </para>
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// The switch from Part 4. With it off, a request is only logged - which is the answer to a
    /// device that keeps knocking, and it costs the DM nothing while he is playing.
    /// </summary>
    public bool AcceptNewDevices { get; set; }

    public IReadOnlyList<PendingPairing> Pending
    {
        get
        {
            lock (_gate)
            {
                return [.. _pending.Values.Select(request => request.Snapshot)];
            }
        }
    }

    public IReadOnlyList<RefusedDevice> Refused
    {
        get
        {
            lock (_gate)
            {
                return [.. _refused.Values];
            }
        }
    }

    public IReadOnlyList<PairedDevice> Paired
    {
        get
        {
            lock (_gate)
            {
                return [.. _paired.Values];
            }
        }
    }

    /// <summary>
    /// Decides what happens to an arriving <c>Hello</c>. The four inputs of Part 4, in the order
    /// in which they are told apart.
    /// </summary>
    public Admission Consider(HelloMessage hello, string address)
    {
        ArgumentNullException.ThrowIfNull(hello);

        var admission = Decide(hello, address);

        // Every outcome moves one of the two lists: a request opened, an existing one refreshed
        // with a new address, a refusal filed, or a refusal dropped because a valid token turned
        // up. Saying so unconditionally is cheaper than working out which of the four it was.
        Changed?.Invoke();

        return admission;
    }

    private Admission Decide(HelloMessage hello, string address)
    {
        var now = _time.GetLocalNow();

        lock (_gate)
        {
            // A valid token goes in without a question. Looked up by device and compared in
            // constant time, so a wrong guess gives nothing away (Part 4).
            var unknownToken = false;

            if (hello.Token is not null)
            {
                if (_paired.TryGetValue(hello.DeviceId, out var known)
                    && DeviceTokens.Matches(hello.Token, known.Token))
                {
                    _refused.Remove(hello.DeviceId);
                    return new Admission.Admitted(known);
                }

                // NOT turned away - it falls through into the ordinary pairing path below.
                // Rejecting was the old answer, and it led nowhere: the way out it pointed at is a
                // hand at a machine that has no keyboard, and after a replaced control.json that
                // would be every display in the flat (Part 4).
                //
                // Nothing is loosened by it. Everything below still applies - a device the DM
                // rejected stays rejected, the gate "accept new devices" still holds, and the rate
                // limit still counts this as a failed attempt, so token guessing is no cheaper
                // than it was.
                unknownToken = true;
            }

            // Once rejected, a device stays rejected until the DM takes it back. Asking him again
            // every five minutes would make the decision worthless.
            if (_refused.TryGetValue(hello.DeviceId, out var refused)
                && refused.Reason == RejectionReason.Denied)
            {
                Note(address, now);
                return Refuse(hello, RejectionReason.Denied, now);
            }

            if (!AcceptNewDevices)
            {
                Note(address, now);
                return Refuse(hello, RejectionReason.Denied, now);
            }

            if (TooManyAttempts(address, now))
            {
                return Refuse(hello, RejectionReason.LimitExceeded, now);
            }

            Note(address, now);

            // A second Hello with the same DeviceId UPDATES the open request instead of laying a
            // second one beside it - an unpaired device on weak Wi-Fi comes back every few
            // seconds (Part 4).
            if (_pending.TryGetValue(hello.DeviceId, out var existing))
            {
                existing.Snapshot = existing.Snapshot with
                {
                    Name = hello.Name,
                    Address = address,
                    PairingCode = hello.PairingCode ?? existing.Snapshot.PairingCode,
                    BroughtUnknownToken = unknownToken,
                };

                return new Admission.Waiting(existing, IsNew: false);
            }

            if (_pending.Count >= _options.MaxOpenPairingRequests)
            {
                return Refuse(hello, RejectionReason.LimitExceeded, now);
            }

            return new Admission.Waiting(
                Open(hello, address, now, isClone: false, unknownToken),
                IsNew: true);
        }
    }

    /// <summary>
    /// Lays a device that turned out to be a clone in front of the DM instead of turning it away.
    /// Cloning a disk is the usual way to set up a second display PC, and a dead end there could
    /// only be left by hand-editing <c>display.json</c> - on a machine without a keyboard
    /// (Part 4, Part 7).
    /// </summary>
    public PendingRequest NoteClone(HelloMessage hello, string address)
    {
        ArgumentNullException.ThrowIfNull(hello);

        var now = _time.GetLocalNow();

        lock (_gate)
        {
            return _pending.TryGetValue(hello.DeviceId, out var existing) && existing.Snapshot.IsClone
                ? existing
                : Open(hello, address, now, isClone: true);
        }
    }

    /// <summary>
    /// Lets a waiting device in with the token the control has already encrypted and written.
    /// <para>
    /// The order is the whole of the promise: the file is on disk BEFORE the <c>Welcome</c> can
    /// go out, because the <c>Welcome</c> is sent from here. A control that crashed in between
    /// would otherwise have a display holding a token that nobody remembers (Part 7).
    /// </para>
    /// </summary>
    public bool Approve(DeviceId device, string token, PairingRole role)
    {
        bool settled;

        lock (_gate)
        {
            if (!_pending.TryGetValue(device, out var request) || request.Snapshot.IsClone)
            {
                return false;
            }

            var paired = new PairedDevice(device, request.Snapshot.Name, role, token);

            _paired[device] = paired;
            _refused.Remove(device);
            _pending.Remove(device);

            settled = request.Settle(new PairingDecision.Approved(paired));
        }

        Changed?.Invoke();

        return settled;
    }

    /// <summary>Turns a waiting device away and keeps it visible with its reason.</summary>
    public bool Reject(DeviceId device)
    {
        var now = _time.GetLocalNow();
        bool settled;

        lock (_gate)
        {
            if (!_pending.TryGetValue(device, out var request))
            {
                return false;
            }

            _pending.Remove(device);

            // A rejected CLONE leaves no entry: the DeviceId belongs to the machine that is
            // legitimately connected under it, and a refusal filed there would blame the wrong
            // one - and would stand in the device list next to a device that is working fine.
            if (!request.Snapshot.IsClone)
            {
                _refused[device] = new RefusedDevice(device, request.Snapshot.Name, RejectionReason.Denied, now);
            }

            settled = request.Settle(new PairingDecision.Refused(RejectionReason.Denied));
        }

        Changed?.Invoke();

        return settled;
    }

    /// <summary>
    /// Tells a clone to take a fresh identity and come back. The device makes the new
    /// <c>DeviceId</c> itself - the control only says that the old one collides, which keeps the
    /// rule that every device creates its own identity (Part 3).
    /// </summary>
    public bool AcceptAsOwnDevice(DeviceId device)
    {
        bool settled;

        lock (_gate)
        {
            if (!_pending.TryGetValue(device, out var request) || !request.Snapshot.IsClone)
            {
                return false;
            }

            _pending.Remove(device);

            settled = request.Settle(new PairingDecision.Refused(RejectionReason.DuplicateDevice));
        }

        Changed?.Invoke();

        return settled;
    }

    /// <summary>Withdraws the token. The device is a stranger again the next time it knocks.</summary>
    public bool Unpair(DeviceId device)
    {
        bool removed;

        lock (_gate)
        {
            removed = _paired.Remove(device);
        }

        if (removed)
        {
            Changed?.Invoke();
        }

        return removed;
    }

    /// <summary>
    /// Takes a rejection back. There is nothing to allow - the <c>Rejected</c> ended the
    /// connection - so the entry is dropped and the device's next attempt becomes an ordinary
    /// request. Without this a mistaken "no" could only be healed at the device (Part 4).
    /// </summary>
    public bool ClearRejection(DeviceId device)
    {
        bool removed;

        lock (_gate)
        {
            removed = _refused.Remove(device);
        }

        if (removed)
        {
            Changed?.Invoke();
        }

        return removed;
    }

    /// <summary>
    /// The connection behind a waiting request went away. Nothing is left behind, which is why
    /// what stands in the list is always what is knocking right now (Part 4).
    /// </summary>
    public void Withdraw(PendingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var withdrawn = false;

        lock (_gate)
        {
            if (_pending.TryGetValue(request.Snapshot.Device, out var current) && current == request)
            {
                withdrawn = _pending.Remove(request.Snapshot.Device);
            }
        }

        if (withdrawn)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Whether this token is good for this role. A display token at the control endpoint is
    /// refused and the other way round: a compromised display PC gets no authority over the
    /// session (Part 4).
    /// </summary>
    public bool Authorises(DeviceId device, string? token, PairingRole role)
    {
        lock (_gate)
        {
            return _paired.TryGetValue(device, out var known)
                && known.Role == role
                && DeviceTokens.Matches(token, known.Token);
        }
    }

    private PendingRequest Open(
        HelloMessage hello,
        string address,
        DateTimeOffset now,
        bool isClone,
        bool unknownToken = false)
    {
        var request = new PendingRequest(new PendingPairing(
            hello.DeviceId,
            hello.Name,
            hello.PairingCode ?? "----",
            address,
            now,
            isClone,
            unknownToken));

        _pending[hello.DeviceId] = request;

        return request;
    }

    private Admission.Refused Refuse(HelloMessage hello, RejectionReason reason, DateTimeOffset now)
    {
        _refused[hello.DeviceId] = new RefusedDevice(hello.DeviceId, hello.Name, reason, now);

        return new Admission.Refused(reason);
    }

    /// <summary>
    /// Counts what did NOT get straight in, per address. A display reconnecting with a valid
    /// token never counts - only guessing does, and both flood vectors (many requests, many token
    /// guesses) run through here.
    /// </summary>
    private void Note(string address, DateTimeOffset now)
    {
        if (!_attempts.TryGetValue(address, out var attempts) || now - attempts.Since >= TimeSpan.FromMinutes(1))
        {
            _attempts[address] = new Attempts(now, 1);
            return;
        }

        _attempts[address] = attempts with { Count = attempts.Count + 1 };
    }

    private bool TooManyAttempts(string address, DateTimeOffset now) =>
        _attempts.TryGetValue(address, out var attempts)
        && now - attempts.Since < TimeSpan.FromMinutes(1)
        && attempts.Count >= _options.MaxPairingAttemptsPerAddressPerMinute;

    private sealed record Attempts(DateTimeOffset Since, int Count);
}
