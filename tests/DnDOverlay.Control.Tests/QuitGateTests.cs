using Verdict = DnDOverlay.Control.QuitGate.Verdict;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The X asks instead of deciding (Part 7): once, outside the close that asked, and the window closes for
/// real only when the answer's work is done.
/// </summary>
public sealed class QuitGateTests
{
    /// <summary>The first X is refused and asks; the question is asked after the close, never inside it (M5 review).</summary>
    [Fact]
    public void The_first_close_is_refused_and_asks()
    {
        var gate = new QuitGate();

        Assert.Equal(Verdict.Ask, gate.Closing(sessionEnding: false));
    }

    /// <summary>An X while the question or its work stands asks nothing - no second dialog, no second GoIdle.</summary>
    [Fact]
    public void A_second_close_while_asking_is_refused_without_asking()
    {
        var gate = new QuitGate();

        gate.Closing(sessionEnding: false);

        Assert.Equal(Verdict.Refuse, gate.Closing(sessionEnding: false));
        Assert.Equal(Verdict.Refuse, gate.Closing(sessionEnding: false));
    }

    /// <summary>Cancelled, the next X asks again.</summary>
    [Fact]
    public void After_a_cancel_the_next_close_asks_again()
    {
        var gate = new QuitGate();

        gate.Closing(sessionEnding: false);
        gate.Cancelled();

        Assert.Equal(Verdict.Ask, gate.Closing(sessionEnding: false));
    }

    /// <summary>The answer's work done, the close the window makes itself goes through.</summary>
    [Fact]
    public void After_the_answer_the_close_goes_through()
    {
        var gate = new QuitGate();

        gate.Closing(sessionEnding: false);
        gate.Answered();

        Assert.Equal(Verdict.Close, gate.Closing(sessionEnding: false));
    }

    /// <summary>Windows ending the session closes at once - nobody is there to answer, also while a question stands.</summary>
    [Fact]
    public void The_end_of_a_Windows_session_closes_at_once()
    {
        var gate = new QuitGate();

        Assert.Equal(Verdict.Close, gate.Closing(sessionEnding: true));

        gate.Closing(sessionEnding: false);

        Assert.Equal(Verdict.Close, gate.Closing(sessionEnding: true));
    }
}
