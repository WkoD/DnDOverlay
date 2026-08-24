using DnDOverlay.Core;
using DnDOverlay.Core.Logging;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Hub;

/// <summary>
/// What a surface learns from the hub. It is <b>not</b> the same thing as what goes over
/// <c>/ws/display</c> - it is its own discriminated union, and it is enumerated here in full on
/// purpose.
/// <para>
/// That is no formality: rule 1 makes the control a client of its own hub, using the same path a
/// foreign device would. Whatever is missing from this union cannot be shown in the control at all
/// - the touch trails in the thumbnail and the progress ring hang directly off it (Part 4).
/// </para>
/// <para>
/// <b>M1b carries the part that exists.</b> Scenes, the device tree, the pairing desk and the log
/// are here; the undo labels arrive with the timeline in M5b, <c>AssetProgress</c> with M2,
/// <c>TouchPoints</c> with M3, and <c>Diagnostics</c>, <c>WindowList</c> and <c>WindowResult</c>
/// with M5. Each of them is an added case, never a changed one (rule 7).
/// </para>
/// </summary>
public abstract record SessionEvent
{
    /// <summary>
    /// Which queue this event belongs in, and therefore what may happen to it when a subscriber
    /// cannot keep up.
    /// <para>
    /// The class travels with the event rather than being decided per endpoint: what was transient
    /// on the way from a device stays transient on the way to a second control (Part 4). Everything
    /// M1b publishes is <see cref="SendClass.State"/>, because everything M1b publishes describes
    /// how things stand rather than how they are at this instant.
    /// </para>
    /// </summary>
    public virtual SendClass SendClass => SendClass.State;

    /// <summary>
    /// The first element of every stream, always - and the reason is a sequence one never gets to
    /// see while developing: <b>the hub is a hosted service and listens before the surface stands</b>
    /// (rule 5). An autostarting display PC can connect faster than the Surface builds its stage,
    /// so a <c>Hello</c>, a state take-over and a pairing request can all lie complete before the
    /// first <c>Subscribe</c>. Without an opening picture the surface would see none of it and
    /// would wait for events that are long past.
    /// <para>
    /// It is the same property <c>SceneSnapshot</c> has for a connecting display, only for the
    /// event stream (Part 4).
    /// </para>
    /// </summary>
    public sealed record Opening(
        IReadOnlyList<DeviceView> Devices,
        IReadOnlyList<(ScreenRef Screen, SceneState Scene)> Scenes,
        IReadOnlyList<PendingPairing> Pending,
        IReadOnlyList<RefusedDevice> Refused) : SessionEvent;

    /// <summary>
    /// The device tree, whole. A full list rather than a delta per screen, and deliberately so
    /// while the surface is a list: a whole list is idempotent, so a subscriber that missed one is
    /// right again with the next, and there is no order to get wrong. The delta arrives when there
    /// is a surface that gains from it - the stage of M4, where a patch per item is what keeps the
    /// thumbnails smooth.
    /// <para>
    /// Idempotence is what makes that affordable, and it is relied on: a device leaving moves two
    /// sources - the connection list and the presence in the catalogue - so it announces twice, and
    /// only the second of the two carries the whole truth. A reader takes the latest and is right.
    /// </para>
    /// </summary>
    public sealed record DevicesChanged(IReadOnlyList<DeviceView> Devices) : SessionEvent;

    /// <summary>
    /// Who is knocking, and who was turned away. Both in one event because both change at the same
    /// moments - a decision moves an entry from the one list to the other (Part 4).
    /// </summary>
    public sealed record PairingChanged(
        IReadOnlyList<PendingPairing> Pending,
        IReadOnlyList<RefusedDevice> Refused) : SessionEvent;

    /// <summary>
    /// The same patch the displays got, after the command that made it. It is the patch and not the
    /// resulting scene, because a second control has to APPLY it: a full scene per change would
    /// throw away exactly what patches are for.
    /// <para>
    /// A patch is the one event in here that is not idempotent - applying an <c>AddItem</c> twice
    /// makes two items. That is why a subscription is registered and its opening picture taken
    /// under one lock (<see cref="SessionEvents.Open"/>): nothing may slip between the two.
    /// </para>
    /// </summary>
    public sealed record ScenePatched(ScenePatch Patch) : SessionEvent;

    /// <summary>
    /// A screen's whole arrangement was replaced rather than changed - the state take-over out of
    /// a <c>Hello</c>, and later loading a scene. A surface takes it as it comes, without asking
    /// where it was before.
    /// </summary>
    public sealed record SceneReplaced(ScreenRef Screen, SceneState Scene) : SessionEvent;

    /// <summary>
    /// One line, ours or a forwarded one - the surface does not have to tell them apart, because
    /// the record says which source it came from. It is the same stream the file gets, so what the
    /// DM reads on screen he finds again in <c>logs\</c> (Part 8).
    /// </summary>
    public sealed record Logged(LogRecord Record) : SessionEvent;

    /// <summary>
    /// What one device is loading right now, feeding the progress ring on the item (Part 7).
    /// <para>
    /// <b>The first event of this stream that is not state</b>, and therefore the first one the
    /// three-queue ranking does any work for: under load the touch points of M3 will stop getting
    /// a turn while this still does. Until now the classes were declared and the lower queues
    /// empty (Part 4).
    /// </para>
    /// <para>
    /// The device is named <b>here</b> rather than in the message: the hub knows which connection
    /// the reading came in on, and that is the one answer a device cannot get wrong or forge.
    /// </para>
    /// </summary>
    public sealed record AssetProgress(DeviceId Device, IReadOnlyList<AssetLoad> Loads) : SessionEvent
    {
        /// <summary>
        /// One slot, overwritten. A reading from a moment ago is worthless rather than inaccurate,
        /// so a subscriber that fell behind wants the newer one and not both.
        /// </summary>
        public override SendClass SendClass => SendClass.Progress;

        public bool Equals(AssetProgress? other) =>
            other is not null && Device == other.Device && Loads.SequenceEqual(other.Loads);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Device);

            foreach (var load in Loads)
            {
                hash.Add(load);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Every finger on one screen, on its way to the thumbnail (Part 7). The DM sees what somebody
    /// at the table is pointing at, and that is the whole purpose.
    /// <para>
    /// <b>The first event of this stream in rank 4</b>, and therefore the first time the ranking
    /// has anything to protect the rest of it from: a table with four hands on it produces ten of
    /// these a second per screen, and a control that has fallen behind wants none of them and every
    /// patch. Until now the classes were declared here and nothing published below state
    /// (Part 4).
    /// </para>
    /// <para>
    /// The screen carries its device, as every patch does: <c>/ws/control</c> carries every device
    /// over one connection, so an event that named only the screen would be ambiguous the moment
    /// two tables were connected (Part 4).
    /// </para>
    /// </summary>
    public sealed record TouchPoints(ScreenRef Screen, IReadOnlyList<TouchTrail> Touches)
        : SessionEvent, IReplacing<SessionEvent>
    {
        /// <summary>
        /// Rank 4: dropped without a word when a subscriber cannot keep up. A finger position from
        /// a moment ago is not inaccurate, it is worthless.
        /// </summary>
        public override SendClass SendClass => SendClass.Transient;

        /// <summary>
        /// One slot per screen and device. Two tables have two independent sets of fingers, and so
        /// do two screens of one table.
        /// </summary>
        public string Slot => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"touch:{Screen.Device.Value}:{Screen.Screen.Value}");

        /// <inheritdoc cref="TouchTrails.Combine" />
        public SessionEvent Over(SessionEvent waiting, int gapMs) =>
            waiting is TouchPoints older && older.Screen == Screen
                ? new TouchPoints(Screen, TouchTrails.Combine(older.Touches, gapMs, Touches))
                : this;

        /// <inheritdoc cref="TouchTrails.Sent" />
        public SessionEvent? Sent(int waitedMs) =>
            TouchTrails.Sent(Touches, waitedMs) is { } touches
                ? touches == Touches ? this : new TouchPoints(Screen, touches)
                : null;

        public bool Equals(TouchPoints? other) =>
            other is not null && Screen == other.Screen && Touches.SequenceEqual(other.Touches);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Screen);

            foreach (var trail in Touches)
            {
                hash.Add(trail);
            }

            return hash.ToHashCode();
        }
    }
}
