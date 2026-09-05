using System.Windows.Threading;
using TilePoint = System.Windows.Point;

namespace DnDOverlay.Control;

/// <summary>
/// The long press, and the one rule that keeps it from strangling a drag.
/// <para>
/// <b>Part 7 calls this the actual work on this grip.</b> Four movements begin with a finger
/// standing still - taking hold of a picture and placing it deliberately, drawing a selection
/// frame, scrolling the stock, pulling a picture out of it. They do not collide with each other;
/// each of them collides only with the long press, and for that <b>one</b> rule at <b>one</b> place
/// is enough. Two copies of it would be the seam from M2 under another name.
/// </para>
/// <para>
/// <b>The clock runs once per touch, from the moment it goes down, and movement stops it for
/// good.</b> A pause in the middle of a drag does not start it again - otherwise a menu would open
/// while the DM lines a picture up over the target tile, which is precisely the moment he is
/// holding still on purpose (Prüfschritt 25e).
/// </para>
/// <para>
/// <b>So a menu needs a fresh grip: let go and put the finger down again.</b> That is the single
/// sentence anybody has to remember when a drag once did not do what was wanted.
/// </para>
/// <para>
/// <b>One case remains and is named rather than fixed</b> (Part 7): whoever takes hold of a picture
/// and rests before the first movement gets a menu. Once the drag is running it is safe, and
/// tapping a menu away costs a tap and changes nothing.
/// </para>
/// <para>
/// <b>It runs for touch alone.</b> The mouse has the right button, and a held left button
/// deliberately does nothing anywhere in this program - "holding" would otherwise mean one thing to
/// a finger and another to a mouse (Part 7).
/// </para>
/// </summary>
internal sealed class Press
{
    /// <summary>
    /// How far a hand may travel before it has moved, in DIP.
    /// <para>
    /// <b>One number for three questions</b>, because all three ask the same thing - did the hand
    /// move? It stops the menu clock, it starts a drag, and below it a gesture is a tap rather than
    /// a frame (Prüfschritt 25a: "a short twitch counts as a tap"). Three numbers would answer one
    /// question three ways, and the two that were never measured would drift apart.
    /// </para>
    /// </summary>
    internal const double Tolerance = 6;

    /// <summary>How long the finger has to stay, in milliseconds (Part 7: about half a second).</summary>
    internal const int HoldMs = 500;

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(HoldMs) };

    private TilePoint _from;
    private Action? _open;

    internal Press() => _clock.Tick += (_, _) => Ring();

    /// <summary>A finger went down. The clock starts here and nowhere else.</summary>
    internal void Down(TilePoint at, Action open)
    {
        _from = at;
        _open = open;

        _clock.Stop();
        _clock.Start();
    }

    /// <summary>
    /// The finger moved. Past the tolerance the menu is off for this touch - <b>and stays off</b>,
    /// which is the half of the rule that a plain "restart the timer" would get wrong.
    /// </summary>
    internal void Moved(TilePoint at)
    {
        if (Math.Abs(at.X - _from.X) + Math.Abs(at.Y - _from.Y) > Tolerance)
        {
            Cancel();
        }
    }

    /// <summary>The finger came up. Nothing is pending any more, whether the menu came or not.</summary>
    internal void Up() => Cancel();

    private void Cancel()
    {
        _clock.Stop();
        _open = null;
    }

    private void Ring()
    {
        var open = _open;

        Cancel();

        open?.Invoke();
    }
}
