using System.Security.Cryptography;
using DnDOverlay.Core;

namespace DnDOverlay.Platform.Windows;

/// <summary>
/// <see cref="ISecretStore"/> over DPAPI, bound to the user profile.
/// <para>
/// This is the one place in the repository that names <c>ProtectedData</c>, and it is here rather
/// than in the hub for a reason that has nothing to do with tidiness: pairing is hub business,
/// and the hub builds for <c>net10.0</c>. A <c>ProtectedData</c> there would compile without a
/// murmur and be a Windows-only API in a library that must not carry one - the architecture test
/// forbids it by name (Part 4, Part 11).
/// </para>
/// <para>
/// <b>What it protects against, honestly:</b> another user on the same machine, a profile copied
/// to a different one, and a disk read outside Windows. <b>Not</b> against a program running as
/// the same user - that one can simply ask DPAPI as well. Additional entropy would not change
/// that, since it would have to sit in our own binary; leaving it out keeps the file readable
/// about what it does. On a display PC with autologon this is exactly the line that matters: the
/// token is worthless to somebody who copies <c>display.json</c> onto a stick.
/// </para>
/// </summary>
public sealed class WindowsSecretStore : ISecretStore
{
    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

    /// <summary>
    /// Decrypts, and answers rather than throws.
    /// <para>
    /// A ciphertext from a different profile is an ordinary situation - a copied installation, a
    /// restored backup, a reinstalled Windows - and it has exactly the same consequence as a
    /// missing token: the device pairs again. Turning that into an exception from the depths
    /// would make a routine event look like a fault (Part 4).
    /// </para>
    /// </summary>
    public bool TryUnprotect(byte[] ciphertext, out byte[] plaintext)
    {
        try
        {
            plaintext = ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = [];
            return false;
        }
    }
}
