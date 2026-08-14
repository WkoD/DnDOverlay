namespace DnDOverlay.Transport;

/// <summary>
/// How long to wait before trying again: one second, doubling to thirty, <b>with spread</b>.
/// <para>
/// The spread is not cosmetic. After a control restarts, every display in the house has lost its
/// connection at the same moment - without it they would all knock again in the same instant, and
/// then again together two seconds later. It is the difference between a queue and a stampede
/// (Part 4).
/// </para>
/// <para>
/// A separate type, because it is the part of reconnecting that can be checked without a network:
/// the growth, the ceiling, and that a connection which worked starts the next one at one second
/// again.
/// </para>
/// </summary>
public sealed class ReconnectBackoff
{
    private static readonly TimeSpan First = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Rejection = TimeSpan.FromMinutes(5);

    private readonly Random _spread;

    private TimeSpan _next = First;

    public ReconnectBackoff(int? seed = null) =>
        _spread = seed is null ? Random.Shared : new Random(seed.Value);

    /// <summary>
    /// The wait before the next attempt, and it moves the next one along. Between 80 % and 120 %
    /// of the plain value, so a house full of displays spreads out by itself.
    /// </summary>
    public TimeSpan Next()
    {
        var plain = _next;

        _next = plain >= Ceiling ? Ceiling : Min(plain * 2, Ceiling);

        return plain * (0.8 + (_spread.NextDouble() * 0.4));
    }

    /// <summary>
    /// A connection that came up starts the count again. Without this a display that reconnects
    /// once an evening would, by the end of the night, take half a minute to come back from a
    /// two-second hiccup.
    /// </summary>
    public void Succeeded() => _next = First;

    /// <summary>
    /// The wait after a control said no - five minutes, and deliberately not the growing one.
    /// <para>
    /// A refusal is a decision, and a second does not change it. Knocking again straight away
    /// gets the device no further and says nothing new; what it does is write the same refusal
    /// into both logs until the rejection looks like a defect. Measured in a hand run, where the
    /// refused device came back about once a second and buried everything else.
    /// </para>
    /// <para>
    /// It neither grows nor resets the ordinary count, because what it waits for is a person
    /// rather than a network - long enough that a rejection is felt, short enough that the DM
    /// changing his mind is not spent waiting (Part 4). The spread stays for the reason it exists
    /// at all: several devices can be turned away in the same minute.
    /// </para>
    /// </summary>
    public TimeSpan Refused() => Rejection * (0.8 + (_spread.NextDouble() * 0.4));

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
