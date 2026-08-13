using System.Collections;
using System.Runtime.InteropServices;

namespace DnDOverlay.Platform.Windows;

/// <summary>
/// Which networks this machine is connected to, and how each one is classified.
/// <para>
/// The firewall itself answers only <c>CurrentProfileTypes</c> - the OR of everything in force. On
/// a docked Surface that is regularly <c>private|public</c> at once, and from that alone nobody can
/// say WHICH network is the public one. The DM has to know: he wants to let his table network
/// through and leave the guest Wi-Fi - or, as measured here, a VPN adapter - alone (Part 7).
/// </para>
/// <para>
/// <b>The rule stays profile-scoped all the same.</b> Choosing a network is a way of choosing a
/// profile, not a rule per adapter: <c>netsh</c> cannot bind a rule to a named adapter at all, and
/// going around it would mean a second COM surface inside an elevated helper. Where two connected
/// networks share a category they cannot be told apart by a rule - and the view says so rather than
/// pretending otherwise (Part 9).
/// </para>
/// </summary>
public static class NetworkList
{
    /// <summary>
    /// <c>NetworkListManager</c>. Addressed by CLSID because there is no ProgID that resolves -
    /// measured: <c>HNetCfg.NetworkListManager</c> answers "class not registered".
    /// </summary>
    private static readonly Guid ManagerClsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    /// <summary><c>NLM_ENUM_NETWORK_CONNECTED</c> - the only ones worth showing.</summary>
    private const int Connected = 1;

    /// <summary>
    /// The connected networks, or an empty list when the service cannot be asked. Empty is a
    /// tolerable answer: the view falls back to the profile bitmask, which is what it showed
    /// before this existed.
    /// </summary>
    public static IReadOnlyList<NetworkView> Current()
    {
        object? manager = null;

        try
        {
            manager = Com.Create(ManagerClsid);

            return manager is null ? [] : Read(Com.Call(manager, "GetNetworks", Connected));
        }
        catch (COMException)
        {
            return [];
        }
        catch (MissingMemberException)
        {
            return [];
        }
        finally
        {
            Com.Release(manager);
        }
    }

    private static List<NetworkView> Read(object? networks)
    {
        var found = new List<NetworkView>();

        if (networks is not IEnumerable collection)
        {
            return found;
        }

        try
        {
            foreach (var network in collection)
            {
                try
                {
                    if (Com.Call(network, "GetName") is not string name)
                    {
                        continue;
                    }

                    found.Add(new NetworkView(name, Category(Com.Number(Com.Call(network, "GetCategory")))));
                }
                finally
                {
                    Com.Release(network);
                }
            }
        }
        finally
        {
            Com.Release(networks);
        }

        return found;
    }

    /// <summary>
    /// <c>NLM_NETWORK_CATEGORY</c> onto the firewall's own flags. Two enumerations for the same
    /// idea, numbered differently - translated here, once, so that nothing above this line has to
    /// know there were ever two.
    /// </summary>
    private static FirewallProfiles Category(int category) => category switch
    {
        0 => FirewallProfiles.Public,
        1 => FirewallProfiles.Private,
        2 => FirewallProfiles.Domain,
        _ => FirewallProfiles.None,
    };
}
