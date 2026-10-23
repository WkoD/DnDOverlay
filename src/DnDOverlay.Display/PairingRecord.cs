using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Display;

/// <summary>
/// What this device's pairing record in display.json becomes on the hub's answers: which token the
/// <c>Hello</c> carries, what a <c>Welcome</c> keeps, and what a refusal changes (Part 4, Part 7).
/// <para>
/// <b>The token travels with the control it belongs to</b>: one without the other would be offered
/// to the first hub that answers. And only one refusal touches the record at all - the clone's,
/// whose fresh identity is made here, at the device (Part 3, Part 7). An unknown token leaves the
/// binding where it is: the beacon is unauthenticated, and a forged control answering every
/// <c>Hello</c> with "unknown token" would otherwise unbind every display in the house and then adopt
/// them. It takes a tap at the device, which is the hurdle an attacker on the network cannot take.
/// </para>
/// </summary>
internal static class PairingRecord
{
    /// <summary>
    /// The token the next <c>Hello</c> carries, or <see langword="null"/>: none without the control it
    /// belongs to, and none that does not decrypt - such a token is simply absent, and the device
    /// pairs again.
    /// </summary>
    internal static string? TokenFor(DisplayConfiguration settings, ISecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);

        return settings.ControlId is not null && DeviceTokens.TryRead(secrets, settings.Token, out var stored)
            ? stored
            : null;
    }

    /// <summary>
    /// What a <c>Welcome</c> keeps: a freshly issued token, encrypted, together with the control that
    /// issued it - or <see langword="null"/> when it hands out none and the record stays as it is.
    /// </summary>
    internal static DisplayConfiguration? Welcomed(DisplayConfiguration settings, WelcomeMessage welcome, ISecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(welcome);
        ArgumentNullException.ThrowIfNull(secrets);

        if (welcome.Token is null)
        {
            return null;
        }

        return settings with
        {
            ControlId = welcome.ControlId,
            Token = DeviceTokens.Store(secrets, welcome.Token),
        };
    }

    /// <summary>
    /// What a refusal changes, or <see langword="null"/> for every refusal that changes nothing. A
    /// collision of identities - a cloned disk, the usual way to set up a second display PC - takes
    /// the fresh identity and drops the binding that belonged to the old one.
    /// </summary>
    /// <param name="fresh">The identity to take - made by the caller, so a test can name it.</param>
    internal static DisplayConfiguration? Rejected(DisplayConfiguration settings, RejectionReason reason, Guid fresh)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return reason is RejectionReason.DuplicateDevice
            ? settings with { DeviceId = fresh, ControlId = null, Token = null }
            : null;
    }
}
