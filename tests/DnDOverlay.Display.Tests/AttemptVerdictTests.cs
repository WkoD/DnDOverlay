using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Transport;

namespace DnDOverlay.Display.Tests;

/// <summary>What a connection attempt amounted to decides how soon the next one comes (Part 4).</summary>
public sealed class AttemptVerdictTests
{
    [Fact]
    public void A_welcome_or_a_pending_request_is_worthwhile()
    {
        Assert.Equal(Attempt.Worthwhile, AttemptVerdict.After(new WelcomeMessage(Guid.Empty, "/assets"), Attempt.Fruitless));
        Assert.Equal(Attempt.Worthwhile, AttemptVerdict.After(new PairingPendingMessage("4711"), Attempt.Fruitless));
    }

    /// <summary>A clone has made itself a fresh identity: the next attempt is a new question, asked at once.</summary>
    [Fact]
    public void A_clone_s_refusal_is_worthwhile()
    {
        Assert.Equal(Attempt.Worthwhile, AttemptVerdict.After(new RejectedMessage(RejectionReason.DuplicateDevice), Attempt.Fruitless));
    }

    /// <summary>A limit passes on its own, so the growing wait is right for it - it is not a refusal.</summary>
    [Fact]
    public void A_reached_limit_is_fruitless()
    {
        Assert.Equal(Attempt.Fruitless, AttemptVerdict.After(new RejectedMessage(RejectionReason.LimitExceeded), Attempt.Worthwhile));
    }

    /// <summary>A control that said no is taken back only by a person.</summary>
    [Theory]
    [InlineData(RejectionReason.Denied)]
    [InlineData(RejectionReason.InvalidToken)]
    public void Any_other_refusal_is_refused(RejectionReason reason)
    {
        Assert.Equal(Attempt.Refused, AttemptVerdict.After(new RejectedMessage(reason), Attempt.Worthwhile));
    }

    /// <summary>Everything else that arrives - scenes, pings - leaves the verdict as it stood.</summary>
    [Fact]
    public void An_ordinary_message_keeps_the_verdict()
    {
        var snapshot = new SceneSnapshotMessage(new ScreenRef(new DeviceId(Guid.Empty), new ScreenId("table")), SceneState.Empty);

        Assert.Equal(Attempt.Refused, AttemptVerdict.After(snapshot, Attempt.Refused));
        Assert.Equal(Attempt.Worthwhile, AttemptVerdict.After(new GoIdleMessage(), Attempt.Worthwhile));
    }

    /// <summary>Dropped for its own control, heard again while standing at a caller's: start from the top at once.</summary>
    [Fact]
    public void Dropped_for_the_own_control_is_worthwhile()
    {
        Assert.Equal(Attempt.Worthwhile, AttemptVerdict.AtEnd(Attempt.Fruitless, droppedForOwnControl: true));
        Assert.Equal(Attempt.Refused, AttemptVerdict.AtEnd(Attempt.Refused, droppedForOwnControl: false));
    }
}
