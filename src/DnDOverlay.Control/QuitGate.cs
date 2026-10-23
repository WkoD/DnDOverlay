namespace DnDOverlay.Control;

/// <summary>
/// Where quitting stands, and what a close of the main window may do (Part 7: the X asks instead of
/// deciding).
/// <para>
/// <b>Every close is refused until the question is answered</b>, and the question is asked AFTER the
/// refused close, never inside it: WPF throws on a <c>Close</c> or <c>Shutdown</c> while a window is still
/// in its own <c>Closing</c> - which is what every quit ended in when the answer's work finished without
/// yielding (M5 review). <b>One question at a time</b>: a close while the question or its work is under
/// way is refused and asks nothing, or a second dialog, a second <c>GoIdle</c> and a second layout under
/// the same stamp would follow. <b>The end of a Windows session closes at once</b> - nobody is there to
/// answer.
/// </para>
/// </summary>
internal sealed class QuitGate
{
    private Stage _stage;

    /// <summary>What a close of the window does.</summary>
    internal enum Verdict
    {
        /// <summary>The window closes - the question was answered, or Windows is ending the session.</summary>
        Close,

        /// <summary>Refused, and the question is asked once the close is over.</summary>
        Ask,

        /// <summary>Refused, and nothing is asked: the question or its work is under way.</summary>
        Refuse,
    }

    private enum Stage
    {
        Open,
        Asking,
        Done,
    }

    /// <summary>A close arrived. Asking moves the gate on: the next close is refused until the question is settled.</summary>
    internal Verdict Closing(bool sessionEnding)
    {
        if (_stage is Stage.Done || sessionEnding)
        {
            return Verdict.Close;
        }

        if (_stage is Stage.Asking)
        {
            return Verdict.Refuse;
        }

        _stage = Stage.Asking;

        return Verdict.Ask;
    }

    /// <summary>The question was cancelled, or the window went meanwhile: the next X asks again.</summary>
    internal void Cancelled() => _stage = Stage.Open;

    /// <summary>The answer's work is done: the next close goes through.</summary>
    internal void Answered() => _stage = Stage.Done;
}
