namespace DnDOverlay.Platform.Windows;

/// <summary>What one rule means for this program, right now.</summary>
public enum FirewallVerdict
{
    /// <summary>Enabled, ours, covers a profile in force, and nothing blocks - it lets us through.</summary>
    Allows,

    /// <summary>
    /// Blocks this program right now. It beats every allow rule beside it, which is why it is a
    /// verdict of its own rather than "does not apply".
    /// </summary>
    Blocks,

    /// <summary>
    /// Would allow, but a block rule covers the same profile. The state a declined "allow access?"
    /// box leaves behind, and the one that used to be reported as "applies now".
    /// </summary>
    Overruled,

    /// <summary>Covers no profile that is currently in force - the classic "set but does not bite".</summary>
    OtherProfile,

    /// <summary>Switched off. Says nothing about profiles, and is not a block either.</summary>
    Disabled,

    /// <summary>Points at a different executable - a leftover from another path, most likely.</summary>
    OtherProgram,
}

/// <summary>One rule together with what it means.</summary>
public sealed record JudgedRule(FirewallRuleView Rule, FirewallVerdict Verdict);

/// <summary>
/// The judging, and it is deliberately free of Windows: everything here is a function over data
/// that <see cref="Firewall"/> has already read.
/// <para>
/// <b>That split is the whole point.</b> The one defect this view ever had was not in the COM call
/// but here - a comparison over four fields that had forgotten the fifth, so a rule that blocks was
/// reported as "applies now", in green, on a machine where nothing got through. Reading the
/// firewall cannot be tested (it needs Windows and rights); whether a rule BITES cannot be tested
/// at all (loopback never passes the inbound firewall, so no machine can try itself). What is left
/// is this - and it is pure, so it is covered (Part 11).
/// </para>
/// </summary>
public static class FirewallVerdicts
{
    /// <summary>
    /// Judges every rule, with precedence applied once: an enabled block on a live profile beats
    /// every allow, so it is worked out before any rule is judged rather than per rule.
    /// </summary>
    public static IReadOnlyList<JudgedRule> Judge(
        IReadOnlyList<FirewallRuleView> rules,
        FirewallProfiles active,
        string program)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var blocked = rules.Any(rule =>
            Applies(rule, active, program) && rule.Action == FirewallAction.Block);

        return [.. rules.Select(rule => new JudgedRule(rule, Verdict(rule, active, program, blocked)))];
    }

    /// <summary>
    /// Whether anything gets through at all - one allow in force and no block. It is not the same
    /// as "a rule exists", and that difference is what the heading above the list says.
    /// </summary>
    public static bool GetsThrough(IReadOnlyList<JudgedRule> judged)
    {
        ArgumentNullException.ThrowIfNull(judged);

        return judged.Any(rule => rule.Verdict == FirewallVerdict.Allows);
    }

    /// <summary>
    /// Which profiles a rule gets written for when the DM allows one network.
    /// <para>
    /// <b>Private and domain are always in it; public is only ever added.</b> Writing "whatever is
    /// active right now" would be right for one moment and wrong afterwards: a rule set while on a
    /// public network would stop applying the moment the machine comes home - it would work where
    /// we do not want it and not where we do. Adding public leaves the home case untouched, so the
    /// button never has to be pressed twice (Part 9).
    /// </para>
    /// </summary>
    public static FirewallProfiles ToWrite(FirewallProfiles category) =>
        FirewallProfiles.Private
        | FirewallProfiles.Domain
        | (category.HasFlag(FirewallProfiles.Public) ? FirewallProfiles.Public : FirewallProfiles.None);

    /// <summary>
    /// Which of the connected networks a rule with these profiles would cover.
    /// <para>
    /// This is what keeps the per-network buttons honest. Two networks classified the same cannot
    /// be told apart by a profile-scoped rule, so "allow for the cable" may well cover the Wi-Fi as
    /// well - and the button says which, instead of implying a precision it has not got.
    /// </para>
    /// </summary>
    public static IReadOnlyList<NetworkView> Covered(
        IReadOnlyList<NetworkView> networks,
        FirewallProfiles profiles)
    {
        ArgumentNullException.ThrowIfNull(networks);

        return [.. networks.Where(network => (network.Category & profiles) != 0)];
    }

    private static bool Applies(FirewallRuleView rule, FirewallProfiles active, string program) =>
        rule.Enabled
        && string.Equals(rule.Program, program, StringComparison.OrdinalIgnoreCase)
        && (rule.Profiles & active) != 0;

    private static FirewallVerdict Verdict(
        FirewallRuleView rule,
        FirewallProfiles active,
        string program,
        bool blocked)
    {
        // Program first: a rule for something else is not disabled, not on the wrong profile and
        // not overruled - it simply is not about us, and saying anything else invites tidying up
        // the wrong entry.
        if (!string.Equals(rule.Program, program, StringComparison.OrdinalIgnoreCase))
        {
            return FirewallVerdict.OtherProgram;
        }

        if (!rule.Enabled)
        {
            return FirewallVerdict.Disabled;
        }

        if ((rule.Profiles & active) == 0)
        {
            return FirewallVerdict.OtherProfile;
        }

        if (rule.Action == FirewallAction.Block)
        {
            return FirewallVerdict.Blocks;
        }

        return blocked ? FirewallVerdict.Overruled : FirewallVerdict.Allows;
    }
}
