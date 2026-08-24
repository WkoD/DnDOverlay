namespace DnDOverlay.Core.Protocol;

/// <summary>
/// Which queue a message goes into on its way out - and therefore what may happen to it when the
/// socket cannot keep up.
/// <para>
/// This is part of the protocol rather than of either end: the same three classes sit in front of
/// <c>/ws/display</c> and in front of <c>/ws/control</c>, and what was transient on the way from a
/// device stays transient on the way to a second control (Part 4). Stating it once here is what
/// keeps the two from drifting apart.
/// </para>
/// </summary>
public enum SendClass
{
    /// <summary>
    /// Never dropped. If it cannot be queued, the connection is no longer consistent and is
    /// closed - the ordinary reconnect with its <c>Hello</c> and <c>SceneSnapshot</c> restores the
    /// truth (Part 4).
    /// </summary>
    State,

    /// <summary>
    /// Rank 3: the feedback that something is being transferred. One slot, overwritten - a
    /// progress reading from a moment ago is worthless, not inaccurate. It has a queue of its own
    /// so that under load the display which EXPLAINS the load is not the first thing to fall away.
    /// </summary>
    Progress,

    /// <summary>
    /// Rank 4: what describes only the present moment. Dropped without a word when it cannot keep
    /// up, because the alternative - a queue that never discards - would spend a socket on
    /// numbers that overtake one another.
    /// </summary>
    Transient,
}

/// <summary>The one place that says which class a message belongs to.</summary>
public static class SendClasses
{
    /// <summary>
    /// Classifies a message. Anything this build does not know is treated as
    /// <see cref="SendClass.State"/>: losing something that had to arrive is the worse of the two
    /// mistakes, so that is the direction the default errs in.
    /// </summary>
    public static SendClass Of(ProtocolMessage message) => message switch
    {
        // The first message that is not state, and therefore the first one that made the ranking
        // do any work at all. Until M2 the two lower queues were built and empty (Part 4, Part 10).
        AssetProgressMessage => SendClass.Progress,

        // The first thing in rank 4, and the reason the rank exists: ten messages a second from
        // every table, every one of which is worthless the moment the next arrives. Diagnostics,
        // WindowList and SpotlightPulse join it in M4 and M5.
        TouchPointsMessage => SendClass.Transient,
        _ => SendClass.State,
    };
}
