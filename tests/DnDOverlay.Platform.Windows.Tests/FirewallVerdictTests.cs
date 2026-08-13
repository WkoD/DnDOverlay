using DnDOverlay.Platform.Windows;

namespace DnDOverlay.Platform.Windows.Tests;

/// <summary>
/// What a firewall rule means for this program. Every case here is a function over data - no
/// firewall is asked and none is changed, which is precisely why these can exist: reading the
/// firewall needs Windows and rights, and whether a rule BITES cannot be established by any
/// machine on its own, because loopback never passes the inbound firewall (Part 11).
/// <para>
/// The first test is the one this file was written for. Its case was measured on a real machine:
/// a dismissed "allow access?" box leaves a BLOCK rule behind, and the view reported it in green
/// as "applies now" while nothing got through.
/// </para>
/// </summary>
public sealed class FirewallVerdictTests
{
    private const string Ours = @"C:\Programs\DnDOverlay\DnDOverlay.Control.exe";
    private const string Other = @"C:\Elsewhere\DnDOverlay.Control.exe";

    [Fact]
    public void A_block_that_covers_the_live_profile_is_reported_as_blocking()
    {
        var judged = Judge([Rule(FirewallAction.Block, FirewallProfiles.Private)], FirewallProfiles.Private);

        Assert.Equal(FirewallVerdict.Blocks, judged.Single().Verdict);
        Assert.False(FirewallVerdicts.GetsThrough(judged));
    }

    /// <summary>
    /// The state a declined prompt plus a pressed "set the rule" leaves behind. Both rules are
    /// enabled, both are ours, both cover the profile in force - and nothing gets through.
    /// </summary>
    [Fact]
    public void An_allow_beside_a_block_is_overruled()
    {
        var judged = Judge(
            [Rule(FirewallAction.Block, FirewallProfiles.Private),
             Rule(FirewallAction.Allow, FirewallProfiles.Private | FirewallProfiles.Domain)],
            FirewallProfiles.Private);

        Assert.Equal(FirewallVerdict.Blocks, judged[0].Verdict);
        Assert.Equal(FirewallVerdict.Overruled, judged[1].Verdict);
        Assert.False(FirewallVerdicts.GetsThrough(judged));
    }

    [Fact]
    public void An_allow_on_its_own_applies()
    {
        var judged = Judge(
            [Rule(FirewallAction.Allow, FirewallProfiles.Private | FirewallProfiles.Domain)],
            FirewallProfiles.Private);

        Assert.Equal(FirewallVerdict.Allows, judged.Single().Verdict);
        Assert.True(FirewallVerdicts.GetsThrough(judged));
    }

    /// <summary>
    /// The classic "set but does not bite" - and the reason the view names the active profile at
    /// all.
    /// </summary>
    [Fact]
    public void A_rule_for_a_profile_that_is_not_in_force_does_not_apply()
    {
        var judged = Judge(
            [Rule(FirewallAction.Allow, FirewallProfiles.Private | FirewallProfiles.Domain)],
            FirewallProfiles.Public);

        Assert.Equal(FirewallVerdict.OtherProfile, judged.Single().Verdict);
        Assert.False(FirewallVerdicts.GetsThrough(judged));
    }

    /// <summary>
    /// A switched-off block blocks nothing, so the allow beside it stands. Without the order in
    /// <c>Verdict</c> this would come out as "blocks" and send somebody hunting for a rule that
    /// does nothing.
    /// </summary>
    [Fact]
    public void A_disabled_block_overrules_nothing()
    {
        var judged = Judge(
            [Rule(FirewallAction.Block, FirewallProfiles.Private, enabled: false),
             Rule(FirewallAction.Allow, FirewallProfiles.Private)],
            FirewallProfiles.Private);

        Assert.Equal(FirewallVerdict.Disabled, judged[0].Verdict);
        Assert.Equal(FirewallVerdict.Allows, judged[1].Verdict);
        Assert.True(FirewallVerdicts.GetsThrough(judged));
    }

    /// <summary>
    /// A block for a DIFFERENT executable is none of our business - it is neither a block of ours
    /// nor a reason to call our own rule overruled. Rules for other paths are the normal leftover
    /// after a build folder changes, so this is not an edge case.
    /// </summary>
    [Fact]
    public void A_block_for_another_program_leaves_our_rule_alone()
    {
        var judged = FirewallVerdicts.Judge(
            [new FirewallRuleView("stranger", Other, true, FirewallProfiles.Private, FirewallAction.Block),
             Rule(FirewallAction.Allow, FirewallProfiles.Private)],
            FirewallProfiles.Private,
            Ours);

        Assert.Equal(FirewallVerdict.OtherProgram, judged[0].Verdict);
        Assert.Equal(FirewallVerdict.Allows, judged[1].Verdict);
        Assert.True(FirewallVerdicts.GetsThrough(judged));
    }

    /// <summary>
    /// A docked Surface is regularly private and public at once - measured. One matching profile
    /// is enough for a rule to be in force.
    /// </summary>
    [Fact]
    public void Any_one_of_several_live_profiles_is_enough()
    {
        var judged = Judge(
            [Rule(FirewallAction.Allow, FirewallProfiles.Private | FirewallProfiles.Domain)],
            FirewallProfiles.Private | FirewallProfiles.Public);

        Assert.Equal(FirewallVerdict.Allows, judged.Single().Verdict);
    }

    [Fact]
    public void Nothing_at_all_gets_nothing_through()
    {
        Assert.False(FirewallVerdicts.GetsThrough(Judge([], FirewallProfiles.Private)));
    }

    private static IReadOnlyList<JudgedRule> Judge(
        IReadOnlyList<FirewallRuleView> rules,
        FirewallProfiles active) =>
        FirewallVerdicts.Judge(rules, active, Ours);

    private static FirewallRuleView Rule(
        FirewallAction action,
        FirewallProfiles profiles,
        bool enabled = true) =>
        new("DnDOverlay Control", Ours, enabled, profiles, action);
}

/// <summary>
/// Which profiles a rule is written for when the DM allows one network, and which networks the
/// result then covers.
/// </summary>
public sealed class FirewallProfileChoiceTests
{
    /// <summary>
    /// The correction that makes the per-network buttons safe to press in any order: home and
    /// domain are never left out, so allowing a public network cannot take away what allowing the
    /// cable had granted.
    /// </summary>
    [Theory]
    [InlineData(FirewallProfiles.Private)]
    [InlineData(FirewallProfiles.Domain)]
    [InlineData(FirewallProfiles.Public)]
    [InlineData(FirewallProfiles.None)]
    public void Private_and_domain_are_always_written(FirewallProfiles category)
    {
        var written = FirewallVerdicts.ToWrite(category);

        Assert.True(written.HasFlag(FirewallProfiles.Private));
        Assert.True(written.HasFlag(FirewallProfiles.Domain));
    }

    /// <summary>
    /// Public is ADDED, never substituted. Writing "whatever is active right now" would produce a
    /// rule that works in every hotel and stops working the moment the machine comes home.
    /// </summary>
    [Fact]
    public void Public_is_added_and_only_for_a_public_network()
    {
        Assert.True(FirewallVerdicts.ToWrite(FirewallProfiles.Public).HasFlag(FirewallProfiles.Public));
        Assert.False(FirewallVerdicts.ToWrite(FirewallProfiles.Private).HasFlag(FirewallProfiles.Public));
    }

    /// <summary>
    /// The honesty of the per-network grip. Two networks classified the same cannot be told apart
    /// by a profile-scoped rule - netsh cannot bind a rule to a named adapter - so the button says
    /// which networks it will really cover instead of implying it covers one.
    /// </summary>
    [Fact]
    public void Two_networks_of_the_same_category_cannot_be_told_apart()
    {
        IReadOnlyList<NetworkView> networks =
        [
            new("leviathan", FirewallProfiles.Private),
            new("workshop", FirewallProfiles.Private),
        ];

        var covered = FirewallVerdicts.Covered(networks, FirewallVerdicts.ToWrite(FirewallProfiles.Private));

        Assert.Equal(2, covered.Count);
    }

    [Fact]
    public void A_home_rule_does_not_cover_a_public_network()
    {
        IReadOnlyList<NetworkView> networks =
        [
            new("leviathan", FirewallProfiles.Private),
            new("guest wifi", FirewallProfiles.Public),
        ];

        var covered = FirewallVerdicts.Covered(networks, FirewallVerdicts.ToWrite(FirewallProfiles.Private));

        Assert.Equal("leviathan", Assert.Single(covered).Name);
    }
}
