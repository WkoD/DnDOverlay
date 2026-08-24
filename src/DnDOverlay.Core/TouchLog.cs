using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Core;

/// <summary>
/// What one screen's fingers have been doing since the last time anybody asked.
/// <para>
/// It sits between two threads on purpose. The window writes into it from the UI thread, where
/// the touch events are; a timer drains it from a background one, so the ten reports a second
/// never queue behind a hand. <b>That is the whole reason it is not simply built in the window</b>
/// - a dispatcher hop at 10 Hz is exactly the kind of traffic that made a load stutter in M3b, and
/// there is nothing here that needs the UI thread once the coordinates are normalised.
/// </para>
/// <para>
/// Nothing is drawn from it at the display. The trails are collected and reported and that is all;
/// what they are for is the thumbnail in the control (Part 1, Part 4).
/// </para>
/// </summary>
public sealed class TouchLog(TimeProvider time)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<long, Finger> _fingers = [];

    /// <summary>
    /// Whether the empty list is owed - the last finger has lifted and nobody has been told yet.
    /// It is a statement rather than an absence, so it goes out once and is not repeated (Part 4).
    /// </summary>
    private bool _lifted;

    /// <summary>
    /// A finger touched down, or moved, at a place given as a fraction of the screen.
    /// <para>
    /// Both are the same event here: the identity is what separates two people, and whether this
    /// point is the first of a trail or the tenth changes nothing about what is recorded.
    /// </para>
    /// </summary>
    public void Moved(long touch, double x, double y)
    {
        lock (_gate)
        {
            if (!_fingers.TryGetValue(touch, out var finger))
            {
                finger = new Finger();
                _fingers[touch] = finger;
            }

            finger.Add(x, y, time.GetTimestamp());
        }
    }

    /// <summary>
    /// A finger left the screen. Its last points stay until they have been reported - where it
    /// went on the way up is as much part of the gesture as the rest.
    /// </summary>
    public void Lifted(long touch, double x, double y)
    {
        lock (_gate)
        {
            if (!_fingers.TryGetValue(touch, out var finger))
            {
                return;
            }

            finger.Add(x, y, time.GetTimestamp());
            finger.Gone = true;
        }
    }

    /// <summary>
    /// Everything since the last call, as one message - or <see langword="null"/> when there is
    /// nothing to say, which is the ordinary state of a table nobody is touching.
    /// <para>
    /// A finger that has not moved still reports, at the place it is resting: it is there NOW, and
    /// a receiver that heard nothing would let it decay and draw the table as empty while somebody
    /// is holding a spot on the map.
    /// </para>
    /// </summary>
    public TouchPointsMessage? Take(ScreenId screen)
    {
        var now = time.GetTimestamp();

        lock (_gate)
        {
            var trails = new List<TouchTrail>(_fingers.Count);

            foreach (var (touch, finger) in _fingers)
            {
                if (finger.Drain(now, time) is { Count: > 0 } points)
                {
                    trails.Add(new TouchTrail(touch, points));
                }
            }

            foreach (var gone in _fingers.Where(entry => entry.Value.Gone).Select(entry => entry.Key).ToList())
            {
                _ = _fingers.Remove(gone);
            }

            if (_fingers.Count == 0 && trails.Count > 0)
            {
                // The last finger has gone, but its own points go out first: the empty list is
                // owed for the next round, so it says "and now nobody" rather than losing the end
                // of the movement to say it.
                _lifted = true;
            }

            if (trails.Count > 0)
            {
                return new TouchPointsMessage(screen, trails);
            }

            if (!_lifted)
            {
                return null;
            }

            _lifted = false;

            return new TouchPointsMessage(screen, []);
        }
    }

    /// <summary>
    /// One finger's points, oldest first, with the moment each was touched. Absolute rather than
    /// aged, because an age only means something against the moment of sending, and that moment is
    /// not known yet when the point is written down.
    /// </summary>
    private sealed class Finger
    {
        private readonly List<(double X, double Y, long At)> _points = [];

        private (double X, double Y)? _resting;

        /// <summary>Whether this finger has left; it is forgotten once its trail is reported.</summary>
        internal bool Gone { get; set; }

        internal void Add(double x, double y, long at)
        {
            _points.Add((x, y, at));

            if (_points.Count > TouchTrail.MaxPoints)
            {
                // From the front: the trail gets shorter, never the message bigger (Part 4).
                _points.RemoveRange(0, _points.Count - TouchTrail.MaxPoints);
            }
        }

        internal List<TouchPoint> Drain(long now, TimeProvider time)
        {
            if (_points.Count > 0)
            {
                _resting = (_points[^1].X, _points[^1].Y);
            }
            else if (!Gone && _resting is { } still)
            {
                // Still down and not moving. Age zero, because that is the truth: the finger is at
                // this place at this moment, and it is the movement that has stopped rather than
                // the finger that has gone.
                return [new TouchPoint(still.X, still.Y, 0)];
            }

            var points = _points
                .Select(point => new TouchPoint(
                    point.X,
                    point.Y,
                    (int)time.GetElapsedTime(point.At, now).TotalMilliseconds))
                .ToList();

            _points.Clear();

            return points;
        }
    }
}
