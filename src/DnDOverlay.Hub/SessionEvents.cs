using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Hub;

/// <summary>
/// Hands each subscriber its own stream of <see cref="SessionEvent"/>.
/// <para>
/// <b>Its own, not a shared one</b>, and that is the whole reason this type exists: with two
/// control devices a shared stream would have the second taking the first one's events away
/// (Part 4). One subscriber that stops reading therefore costs nobody else anything.
/// </para>
/// <para>
/// Each stream carries the same three ranks a socket does, and for the same reason: what was
/// transient coming from a device stays transient going to a control (Part 4). One channel would
/// not do it - a table with four hands on it fills 256 slots with finger positions in a few
/// seconds, and the next patch would then end a stream that was merely busy rather than lost.
/// </para>
/// </summary>
/// <param name="time">
/// What the ageing of the transient rank is measured on. It is the ordinary clock: these events go
/// to a surface in this process, so a wait here is a real wait.
/// </param>
public sealed class SessionEvents(TimeProvider? time = null)
{
    /// <summary>
    /// Generous enough for the biggest burst a single command makes - a scene with all its items,
    /// a layout across every screen. A subscriber that is this far behind is not going to catch up,
    /// and holding more would only put off the moment at which that becomes true.
    /// </summary>
    internal const int Capacity = 256;

    /// <summary>
    /// How many kinds of transient may wait: touch points per screen, and from M5 the diagnostics
    /// and the window list. Each kind has a slot of one, so this bounds the rank by what the build
    /// can produce rather than by a queue length (Part 4).
    /// </summary>
    private const int TransientSlotCount = 16;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly List<Subscription> _subscribers = [];

    /// <summary>
    /// Registers a subscriber and takes its opening picture <b>under one lock</b>.
    /// <para>
    /// The order is not tidiness. Taking the picture first would lose everything that happened
    /// before the registration; registering first would deliver events the picture already contains
    /// - harmless for a whole list, and a real fault for a
    /// <see cref="SessionEvent.ScenePatched"/>, because applying an <c>AddItem</c> twice makes two
    /// items. Doing both under the publishing lock leaves no gap either way.
    /// </para>
    /// </summary>
    public Subscription Open(Func<SessionEvent> opening)
    {
        ArgumentNullException.ThrowIfNull(opening);

        Subscription subscription;

        lock (_gate)
        {
            subscription = new Subscription(this, _time);

            // Cannot fail: the channel is empty and the capacity is not one.
            _ = subscription.Offer(opening());

            _subscribers.Add(subscription);
        }

        subscription.Wake();

        return subscription;
    }

    /// <summary>
    /// Gives every subscriber this event, in the class it belongs to.
    /// <para>
    /// A state event is never dropped. Where it cannot be queued, that subscriber is no longer
    /// consistent and its stream is ENDED rather than served something stale - the same rule that
    /// governs a socket, and with the same way back: subscribing again yields a fresh opening
    /// picture, which is what a <c>SceneSnapshot</c> does for a display (Part 4).
    /// </para>
    /// </summary>
    public void Publish(SessionEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        Subscription[] told;

        lock (_gate)
        {
            foreach (var subscriber in _subscribers)
            {
                if (subscriber.Offer(@event))
                {
                    continue;
                }

                // Progress and transient are dropped where they cannot be taken - that is what the
                // ranks mean, and it is ordinary operation rather than an incident.
                if (@event.SendClass is SendClass.State)
                {
                    subscriber.Finish();
                }
            }

            told = [.. _subscribers];
        }

        // Outside the lock, always: waking a reader is the moment its continuation may run, and
        // under the publishing lock that would be surface code holding up every other subscriber.
        // A wake-up that finds nothing costs a loop.
        foreach (var subscriber in told)
        {
            subscriber.Wake();
        }
    }

    private void Close(Subscription subscription)
    {
        lock (_gate)
        {
            _ = _subscribers.Remove(subscription);
        }
    }

    /// <summary>One reader's stream. Disposing it unregisters; nothing else does.</summary>
    public sealed class Subscription : IDisposable
    {
        private readonly SessionEvents _owner;

        private readonly Channel<SessionEvent> _state = Channel.CreateBounded<SessionEvent>(
            new BoundedChannelOptions(Capacity)
            {
                SingleReader = true,
                SingleWriter = true,

                // Wait rather than a dropping mode, because the mode is a property of the CHANNEL
                // and the decision is a property of the EVENT: with Wait, TryWrite simply says no
                // when it is full, and the caller decides what that means for this class.
                FullMode = BoundedChannelFullMode.Wait,

                // A publisher holds the lock while it writes. Running a reader's continuation
                // inline would run surface code under it.
                AllowSynchronousContinuations = false,
            });

        /// <summary>One slot, overwritten: a reading from a moment ago is worthless, not late.</summary>
        private readonly Channel<SessionEvent> _progress = Channel.CreateBounded<SessionEvent>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false,
            });

        private readonly TransientSlots<SessionEvent> _transient;

        /// <summary>
        /// One signal for all three ranks. Waiting on three channels at once would leave a waiter
        /// behind in every one that did not win the race, and those pile up per iteration.
        /// </summary>
        private readonly SemaphoreSlim _work = new(0);

        private volatile bool _finished;

        internal Subscription(SessionEvents owner, TimeProvider time)
        {
            _owner = owner;
            _transient = new TransientSlots<SessionEvent>(TransientSlotCount, time);
        }

        /// <summary>Everything from the opening picture onwards, until the reader stops or is cut off.</summary>
        public async IAsyncEnumerable<SessionEvent> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                while (TryTake(out var @event))
                {
                    yield return @event!;
                }

                // Checked after the drain, never before: an ended stream still owes its reader
                // whatever was queued before it ended.
                if (_finished)
                {
                    yield break;
                }

                await _work.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        internal bool Offer(SessionEvent @event) => @event.SendClass switch
        {
            SendClass.Transient => _transient.Offer(@event),
            SendClass.Progress => _progress.Writer.TryWrite(@event),
            _ => _state.Writer.TryWrite(@event),
        };

        internal void Finish() => _finished = true;

        /// <summary>Tells a waiting reader to look again. A wake-up that finds nothing costs a loop.</summary>
        internal void Wake()
        {
            try
            {
                _work.Release();
            }
            catch (SemaphoreFullException)
            {
                // More wake-ups than the counter can hold means the reader is far behind and will
                // find everything anyway. Nothing is lost by not counting this one.
            }
        }

        public void Dispose()
        {
            Finish();
            Wake();

            _owner.Close(this);
        }

        /// <summary>
        /// State first, then progress, then what is merely current. The precedence of Part 1 then
        /// arises on its own: a control that has fallen behind stops seeing finger positions while
        /// it still sees every patch, and nothing had to throttle either of them.
        /// </summary>
        private bool TryTake(out SessionEvent? @event) =>
            _state.Reader.TryRead(out @event)
            || _progress.Reader.TryRead(out @event)
            || _transient.TryTake(out @event);
    }
}
