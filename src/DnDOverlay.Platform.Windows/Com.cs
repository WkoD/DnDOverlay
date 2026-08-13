using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DnDOverlay.Platform.Windows;

/// <summary>
/// The late-binding idiom, in one place. Two Windows objects are asked here - the firewall policy
/// and the network list - and both are asked the same way.
/// <para>
/// <b>Late binding rather than declared COM interfaces</b>, and the reason is the vtable: a
/// hand-written <c>[ComImport]</c> declaration has to list every member in exact order, and a
/// mistake there is not a compile error but a call into the wrong slot. Through IDispatch the name
/// is the contract, and a member that is not there raises <see cref="MissingMemberException"/>
/// where it can be answered.
/// </para>
/// <para>
/// <b>The price is named:</b> what IDispatch cannot marshal is simply not obtainable this way.
/// That is why the network list is read by network name and category rather than by adapter -
/// <c>GetAdapterId</c> returns a GUID and throws through this path (measured). It cost nothing,
/// because a network name is what the DM recognises anyway.
/// </para>
/// </summary>
internal static class Com
{
    /// <summary>Creates the object behind a ProgID, or null when it is not registered.</summary>
    internal static object? Create(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: false);

        return type is null ? null : Activator.CreateInstance(type);
    }

    /// <summary>
    /// Creates the object behind a CLSID. Needed where there is no usable ProgID - the network
    /// list is registered under its class identifier alone (measured: <c>HNetCfg.NetworkListManager</c>
    /// does not resolve).
    /// </summary>
    internal static object? Create(Guid clsid)
    {
        var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);

        return type is null ? null : Activator.CreateInstance(type);
    }

    /// <summary>Reads a property by name.</summary>
    internal static object? Get(object target, string member) =>
        target.GetType().InvokeMember(
            member,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);

    /// <summary>Calls a method by name.</summary>
    internal static object? Call(object target, string member, params object[] arguments) =>
        target.GetType().InvokeMember(
            member,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            arguments,
            CultureInfo.InvariantCulture);

    internal static int Number(object? value) =>
        value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    internal static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }
    }
}
