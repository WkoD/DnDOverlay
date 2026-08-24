namespace DnDOverlay.Core.Protocol;

/// <summary>
/// One point of one touch: where the finger was, and how long ago that was.
/// </summary>
/// <param name="X">Across the screen, as a fraction of its width - normalised like the scene (Part 3).</param>
/// <param name="Y">Down the screen, as a fraction of its height.</param>
/// <param name="AgeMs">
/// How long before this message was sent the finger was there.
/// <para>
/// It carries its own age rather than a place in a list, because the fading is what the age
/// drives: a trail assembled out of two messages that were merged would otherwise be uneven at
/// the seam - and the join is exactly where two sending intervals meet (Part 4).
/// </para>
/// </param>
public readonly record struct TouchPoint(double X, double Y, int AgeMs);

/// <summary>
/// One finger's path since the last send.
/// </summary>
/// <param name="Touch">
/// Which finger, from the moment it goes down to the moment it lifts. Without it a receiver could
/// not tell which points belong together and would join two people's lines into one zigzag
/// (Part 4, Part 7).
/// </param>
/// <param name="Points">
/// Where it has been, oldest first. A resting finger leaves one, a fast movement a dozen - a
/// player rarely points at a spot, he traces the way the group should take.
/// </param>
public sealed record TouchTrail(long Touch, IReadOnlyList<TouchPoint> Points)
{
    /// <summary>
    /// At most this many points per trail and message. Beyond it the front falls away: <b>the
    /// trail gets shorter, never the message bigger</b> (Part 4).
    /// <para>
    /// Enough for a few sending intervals of merged trail at the digitizer's sampling rate, which
    /// is what a merge under load produces.
    /// </para>
    /// </summary>
    public const int MaxPoints = 32;

    public bool Equals(TouchTrail? other) =>
        other is not null && Touch == other.Touch && Points.SequenceEqual(other.Points);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Touch);

        foreach (var point in Points)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Every finger on one screen, normalised - not one message per finger. Four people with six
/// fingers make one message with six entries, sent about ten times a second (Part 4).
/// <para>
/// <b>Nothing on the table changes because of it.</b> The trails are collected and reported and
/// drawn nowhere at the display: drawing is not part of DnDOverlay (Part 1). What they are for is
/// the thumbnail in the control - the DM sees what somebody is pointing at, and that is the whole
/// purpose (Part 7, M4).
/// </para>
/// <para>
/// An <b>empty list</b> is a statement rather than nothing to say: it goes out once when the last
/// finger lifts, so a receiver is rid of a ghost without having to wait out the decay.
/// </para>
/// </summary>
public sealed record TouchPointsMessage(ScreenId Screen, IReadOnlyList<TouchTrail> Touches)
    : ProtocolMessage, IReplacing<ProtocolMessage>
{
    /// <summary>
    /// How old the youngest point may be when the message is taken out to be written. Two sending
    /// intervals: beyond that the head of the trail is showing a finger that is no longer there
    /// (Part 4).
    /// </summary>
    public const int SendAgeMs = 200;

    /// <summary>
    /// How long a trail lives at the receiver without being renewed. Longer than one missed
    /// message, short enough that no ghost finger stays behind on a dead connection (Part 4).
    /// <para>
    /// It is stated here, on the message, rather than at the receiver: the sender's 200 ms and the
    /// receiver's 300 ms are one rule with two halves, and half a rule in each of two places is
    /// how they drift apart.
    /// </para>
    /// </summary>
    public const int DecayMs = 300;

    /// <summary>
    /// One slot per screen, not per device: a table with two screens has two independent sets of
    /// fingers on it, and merging them would be a zigzag between two rooms.
    /// </summary>
    public string Slot => string.Concat("touch:", Screen.Value);

    /// <summary>How long ago the most recent point of this message was touched.</summary>
    public int YoungestMs =>
        Touches.Count == 0
            ? 0
            : Touches.Min(trail => trail.Points.Count == 0 ? 0 : trail.Points.Min(point => point.AgeMs));

    /// <summary>
    /// This message laid over the one it displaces, per finger - <b>the trails are combined, not
    /// discarded</b>.
    /// <para>
    /// That is what separates this from every other transient: the delay may be thrown away, the
    /// movement may not. A plain overwrite under load would drop exactly the points that make the
    /// line, and what arrived would be a string of beads with no direction (Part 4).
    /// </para>
    /// <para>
    /// A finger the waiting message knew and this one does not is kept: it lifted between the two
    /// sends, and where it went on its way up is as much part of the gesture as the rest.
    /// </para>
    /// </summary>
    public ProtocolMessage Over(ProtocolMessage waiting, int gapMs)
    {
        // Anything else in this slot is another screen's or another build's, and laying those over
        // one another would invent a trail. Keeping the newer one is the ordinary transient answer.
        if (waiting is not TouchPointsMessage older || older.Screen != Screen)
        {
            return this;
        }

        // The waiting message was built gapMs before this one, so every one of its points is that
        // much older on THIS message's clock. Without the shift, the seam between two merged sends
        // would be the one place where the fading jumps.
        var carried = older.Touches.ToDictionary(trail => trail.Touch, trail => trail.Points);
        var combined = new List<TouchTrail>(carried.Count + Touches.Count);

        foreach (var trail in Touches)
        {
            combined.Add(carried.Remove(trail.Touch, out var before)
                ? new TouchTrail(trail.Touch, Join(before, gapMs, trail.Points))
                : trail);
        }

        // In the waiting message's own order, not the dictionary's: a receiver that draws them in
        // the order they arrive should not see them shuffle from one send to the next.
        foreach (var lifted in older.Touches.Where(trail => carried.ContainsKey(trail.Touch)))
        {
            combined.Add(new TouchTrail(lifted.Touch, Join(lifted.Points, gapMs, [])));
        }

        return new TouchPointsMessage(Screen, combined);
    }

    /// <summary>
    /// The message with every age moved on by what the wait cost - or nothing at all, when even
    /// its youngest point is by now too old to be worth a finger circle where no finger is.
    /// </summary>
    public ProtocolMessage? Sent(int waitedMs)
    {
        // The empty list is the one message a wait cannot spoil: it says a hand LEFT, and that
        // stays true however long it took to get out.
        if (Touches.Count == 0)
        {
            return this;
        }

        if (YoungestMs + waitedMs > SendAgeMs)
        {
            return null;
        }

        return waitedMs == 0
            ? this
            : new TouchPointsMessage(Screen, [.. Touches.Select(trail => new TouchTrail(
                trail.Touch,
                [.. trail.Points.Select(point => point with { AgeMs = point.AgeMs + waitedMs })]))]);
    }

    public bool Equals(TouchPointsMessage? other) =>
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

    /// <summary>
    /// Older points first, aged onto the newer message's clock, then the newer ones - and the cap
    /// taken off the front, so what is lost is the tail of the trail rather than its head.
    /// </summary>
    private static List<TouchPoint> Join(
        IReadOnlyList<TouchPoint> before,
        int gapMs,
        IReadOnlyList<TouchPoint> after)
    {
        var joined = new List<TouchPoint>(before.Count + after.Count);

        joined.AddRange(before.Select(point => point with { AgeMs = point.AgeMs + gapMs }));
        joined.AddRange(after);

        return joined.Count <= TouchTrail.MaxPoints
            ? joined
            : joined[^TouchTrail.MaxPoints..];
    }
}
