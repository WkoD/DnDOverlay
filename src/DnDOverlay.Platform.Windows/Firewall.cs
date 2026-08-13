using System.Collections;
using System.Runtime.InteropServices;

namespace DnDOverlay.Platform.Windows;

/// <summary>Which network profiles a rule covers, and which ones are live right now.</summary>
[Flags]
public enum FirewallProfiles
{
    None = 0,

    Domain = 1,

    /// <summary>What a home network is classified as, and what our rule is written for.</summary>
    Private = 2,

    /// <summary>
    /// What Windows likes to make of a freshly joined network - and therefore the most common
    /// reason a rule that IS set does not bite (Part 9).
    /// </summary>
    Public = 4,

    /// <summary>The value the firewall uses for "all of them".</summary>
    All = 0x7FFFFFFF,
}

/// <summary>
/// What a rule does when it matches - <c>NET_FW_ACTION</c>, and the field whose absence used to
/// make the reachability view say the opposite of the truth.
/// <para>
/// Windows writes a BLOCK rule when the "allow access?" box is dismissed rather than accepted -
/// measured, not assumed - and a block beats every allow. Without this, such a rule was shown as
/// "applies now", in green, on a machine where nothing got through (Part 9).
/// </para>
/// </summary>
public enum FirewallAction
{
    /// <summary><c>NET_FW_ACTION_BLOCK</c>.</summary>
    Block = 0,

    /// <summary><c>NET_FW_ACTION_ALLOW</c>.</summary>
    Allow = 1,
}

/// <summary>One rule of ours, as it stands out there.</summary>
/// <param name="Program">
/// What it lets through. A rule is program-based rather than port-based because the port is
/// configurable, so this is the field that says whether it still points anywhere real (Part 9).
/// </param>
public sealed record FirewallRuleView(
    string Name,
    string? Program,
    bool Enabled,
    FirewallProfiles Profiles,
    FirewallAction Action);

/// <summary>
/// One network this machine is connected to, by the name Windows shows for it and the category it
/// was classified as.
/// <para>
/// <b>Networks rather than adapters</b>, and that is not a compromise: the DM recognises his table
/// network by its name, and the classification - which is what decides whether a rule bites - hangs
/// on the network, not on the card. It is also the only form obtainable through late binding; the
/// adapter identifier is a GUID return value that IDispatch will not hand over.
/// </para>
/// </summary>
public sealed record NetworkView(string Name, FirewallProfiles Category);

/// <summary>
/// What the reachability view found. <see cref="Rules"/> may hold none, one, or several - and
/// "several" is the finding this whole thing exists for: Windows allows duplicate display names,
/// and a repeated <c>add</c> used to leave them behind.
/// </summary>
public sealed record FirewallState(FirewallProfiles Active, IReadOnlyList<FirewallRuleView> Rules)
{
    /// <summary>
    /// When the firewall could not be asked at all - the service is off, or the COM object is not
    /// there. Told apart from "asked, found nothing", because the two mean opposite things to
    /// somebody reading the view.
    /// </summary>
    public static readonly FirewallState Unknown = new(FirewallProfiles.None, []);

    public bool Asked => Active != FirewallProfiles.None || Rules.Count > 0;
}

/// <summary>
/// Reads the Windows firewall: which profiles are active, and which of our rules exist.
/// <para>
/// <b>Reading needs no elevation</b> - only changing does. That is what makes the reachability
/// view answer the question one otherwise has to guess at: does the new rule bite, or is an old
/// one still in the way? Elevation is needed for the tidying up, and that is what the two helpers
/// are for (Part 7, Part 9).
/// </para>
/// <para>
/// <b>Through the firewall's own COM object, never by parsing <c>netsh</c>.</b> netsh speaks the
/// language of the machine it runs on: on a German Windows every line of its output is German, so
/// anything reading it would work in one country. The same object answers both questions, so
/// there is one dependency rather than two.
/// </para>
/// </summary>
public static class Firewall
{
    private const string PolicyProgId = "HNetCfg.FwPolicy2";

    /// <summary>Inbound. The one direction any rule of ours is ever about.</summary>
    private const int Inbound = 1;

    /// <summary>
    /// Every inbound rule that either carries our name or lets our program through, plus the
    /// profiles in force.
    /// <para>
    /// <b>By name prefix</b> rather than exact name, because that catches the development rule
    /// beside the installed one. Which is the live one is not decided here - the view shows what
    /// each points at, which is the only honest answer when there are two.
    /// </para>
    /// <para>
    /// <b>And by program</b>, because Windows writes rules of its own: the "allow access?" box
    /// that pops up the first time a program listens leaves one behind, named after the executable
    /// and scoped to whatever profile was active. Those are invisible to a search by our name and
    /// are frequently the reason something works - or stops working - and the DM would be looking
    /// at the wrong rule.
    /// </para>
    /// </summary>
    public static FirewallState Inspect(string namePrefix, string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(namePrefix);
        ArgumentException.ThrowIfNullOrEmpty(program);

        object? policy = null;

        try
        {
            policy = Com.Create(PolicyProgId);

            if (policy is null)
            {
                return FirewallState.Unknown;
            }

            return new FirewallState(
                (FirewallProfiles)Com.Number(Com.Get(policy, "CurrentProfileTypes")),
                Ours(Com.Get(policy, "Rules"), namePrefix, program));
        }
        catch (COMException)
        {
            // The firewall service is not running, or the object refused. Both mean the same thing
            // to the reader: we could not ask.
            return FirewallState.Unknown;
        }
        catch (MissingMemberException)
        {
            // A Windows build whose object does not carry what we asked for. Said rather than
            // crashed - this is a diagnostic view, not a load-bearing path.
            return FirewallState.Unknown;
        }
        finally
        {
            Com.Release(policy);
        }
    }

    /// <summary>
    /// Every rule is walked, because the collection can only fetch ONE by name - and finding
    /// duplicates is the point.
    /// </summary>
    private static List<FirewallRuleView> Ours(object? rules, string namePrefix, string program)
    {
        var found = new List<FirewallRuleView>();

        if (rules is not IEnumerable collection)
        {
            return found;
        }

        try
        {
            foreach (var rule in collection)
            {
                try
                {
                    if (Com.Get(rule, "Name") is not string name
                        || Com.Number(Com.Get(rule, "Direction")) != Inbound)
                    {
                        continue;
                    }

                    var application = Com.Get(rule, "ApplicationName") as string;

                    // Ours by name, or ours by what it lets through. Paths are compared without
                    // regard to case because Windows writes its own rules in lower case.
                    if (!name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(application, program, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    found.Add(new FirewallRuleView(
                        name,
                        application,
                        Com.Get(rule, "Enabled") is bool enabled && enabled,
                        (FirewallProfiles)Com.Number(Com.Get(rule, "Profiles")),
                        (FirewallAction)Com.Number(Com.Get(rule, "Action"))));
                }
                finally
                {
                    Com.Release(rule);
                }
            }
        }
        finally
        {
            Com.Release(rules);
        }

        return found;
    }
}
