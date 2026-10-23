using DnDOverlay.Core.Protocol;
using DnDOverlay.Transport;

namespace DnDOverlay.Display;

/// <summary>
/// What one connection attempt amounted to, for the loop that decides how soon to try again
/// (<see cref="ConnectionLoop"/>, Part 4).
/// <para>
/// <b>A refusal is not one answer.</b> A clone told its identity collides has made itself a fresh one,
/// so the next attempt is a new question and worth asking at once; a limit that was reached is a
/// state of the hub that passes on its own, so the growing wait is right for it; anything else the
/// control said no to only a person takes back.
/// </para>
/// </summary>
internal static class AttemptVerdict
{
    /// <summary>The attempt after one more message: what it said, or what the attempt was before when it says nothing about that.</summary>
    internal static Attempt After(ProtocolMessage message, Attempt before) =>
        message switch
        {
            WelcomeMessage or PairingPendingMessage => Attempt.Worthwhile,
            RejectedMessage refusal => refusal.Reason switch
            {
                RejectionReason.DuplicateDevice => Attempt.Worthwhile,
                RejectionReason.LimitExceeded => Attempt.Fruitless,
                _ => Attempt.Refused,
            },
            _ => before,
        };

    /// <summary>
    /// The attempt when its connection has ended. Dropped for this device's own control - heard again
    /// while it stood as a request at a caller's - it is worth starting from the top at once: the
    /// beacon is there (Part 4).
    /// </summary>
    internal static Attempt AtEnd(Attempt attempt, bool droppedForOwnControl) =>
        droppedForOwnControl ? Attempt.Worthwhile : attempt;
}
