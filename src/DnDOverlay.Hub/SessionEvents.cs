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
/// </summary>
public sealed class SessionEvents
{
    /// <summary>
    /// Generous enough for the biggest burst a single command makes - a scene with all its items,
    /// a layout across every screen. A subscriber that is this far behind is not going to catch up,
    /// and holding more would only put off the moment at which that becomes true.
    /// </summary>
    internal const int Capacity = 256;

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

        lock (_gate)
        {
            var subscription = new Subscription(this);

            // Cannot fail: the channel is empty and the capacity is not one.
            _ = subscription.Offer(opening());

            _subscribers.Add(subscription);

            return subscription;
        }
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

        lock (_gate)
        {
            foreach (var subscriber in _subscribers)
            {
                if (subscriber.Offer(@event))
                {
                    continue;
                }

                switch (@event.SendClass)
                {
                    case SendClass.State:
                        subscriber.Finish();
                        break;

                    // Progress gets a replacing slot of its own when AssetProgress arrives in M2,
                    // exactly as it has one in front of a socket. Until then nothing publishes in
                    // either class, and dropping is the right answer for both (Part 4).
                    case SendClass.Progress:
                    case SendClass.Transient:
                    default:
                        break;
                }
            }
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

        private readonly Channel<SessionEvent> _events = Channel.CreateBounded<SessionEvent>(
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

        internal Subscription(SessionEvents owner) => _owner = owner;

        /// <summary>Everything from the opening picture onwards, until the reader stops or is cut off.</summary>
        public IAsyncEnumerable<SessionEvent> ReadAllAsync(CancellationToken cancellationToken = default) =>
            _events.Reader.ReadAllAsync(cancellationToken);

        internal bool Offer(SessionEvent @event) => _events.Writer.TryWrite(@event);

        internal void Finish() => _events.Writer.TryComplete();

        public void Dispose()
        {
            Finish();
            _owner.Close(this);
        }
    }
}
