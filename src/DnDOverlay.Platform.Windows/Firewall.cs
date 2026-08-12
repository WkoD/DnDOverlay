using System.Collections;
using System.Globalization;
using System.Reflection;
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

/// <summary>One rule of ours, as it stands out there.</summary>
/// <param name="Program">
/// What it lets through. A rule is program-based rather than port-based because the port is
/// configurable, so this is the field that says whether it still points anywhere real (Part 9).
/// </param>
public sealed record FirewallRuleView(string Name, string? Program, bool Enabled, FirewallProfiles Profiles);

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

        var type = Type.GetTypeFromProgID(PolicyProgId, throwOnError: false);

        if (type is null)
        {
            return FirewallState.Unknown;
        }

        object? policy = null;

        try
        {
            policy = Activator.CreateInstance(type);

            if (policy is null)
            {
                return FirewallState.Unknown;
            }

            return new FirewallState(
                (FirewallProfiles)Number(Get(policy, "CurrentProfileTypes")),
                Ours(Get(policy, "Rules"), namePrefix, program));
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
            Release(policy);
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
                    if (Get(rule, "Name") is not string name || Number(Get(rule, "Direction")) != Inbound)
                    {
                        continue;
                    }

                    var application = Get(rule, "ApplicationName") as string;

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
                        Get(rule, "Enabled") is bool enabled && enabled,
                        (FirewallProfiles)Number(Get(rule, "Profiles"))));
                }
                finally
                {
                    Release(rule);
                }
            }
        }
        finally
        {
            Release(rules);
        }

        return found;
    }

    /// <summary>
    /// Late binding rather than declared COM interfaces, and the reason is the vtable: a hand-written
    /// <c>[ComImport]</c> declaration has to list every member in exact order, and a mistake there is
    /// not a compile error but a call into the wrong slot. Through IDispatch the name is the
    /// contract, and a member that is not there raises <see cref="MissingMemberException"/> where it
    /// can be answered.
    /// </summary>
    private static object? Get(object target, string member) =>
        target.GetType().InvokeMember(
            member,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);

    private static int Number(object? value) =>
        value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }
    }
}
