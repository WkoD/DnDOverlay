using System.Security.Cryptography;
using System.Text;

namespace DnDOverlay.Core;

/// <summary>
/// What a token is allowed to do. Two roles, and a display token presented at the control
/// endpoint is refused - a compromised display PC gets no authority over the session (Part 4).
/// <para>
/// The role is a property of the ENTRY, never of the token string. Parsing a role out of
/// something that arrived over the wire would mean trusting the presenter about what he is
/// allowed to be; kept in the entry, it is read from our own file.
/// </para>
/// </summary>
public enum PairingRole
{
    /// <summary>A display PC: may receive scenes and report gestures.</summary>
    Display,

    /// <summary>A further control device (M8). May drive the session, never widen the circle.</summary>
    Control,
}

/// <summary>
/// Encrypting something at rest, without naming who does it (rule 8).
/// <para>
/// Behind this sits DPAPI, and that is exactly the kind of foreign dependency the rule is made
/// for: it is Windows only, while pairing itself is hub business and the hub builds for
/// <c>net10.0</c>. Three things fall out of the interface, and none of them is portability - the
/// pairing tests run against a fake instead of the runner's crypto stack and become deterministic;
/// the case "this token was written under a different profile" gets an ANSWER instead of an
/// exception from the depths; and changing the protection level later would be one file
/// (Part 4).
/// </para>
/// </summary>
public interface ISecretStore
{
    /// <summary>Encrypts for this machine and this user profile.</summary>
    byte[] Protect(byte[] plaintext);

    /// <summary>
    /// Decrypts what <see cref="Protect"/> wrote. <see langword="false"/> means the ciphertext
    /// does not belong to this profile - the same outcome as a missing token, and deliberately
    /// not an exception: a copied profile is an ordinary situation, not a fault.
    /// </summary>
    bool TryUnprotect(byte[] ciphertext, out byte[] plaintext);
}

/// <summary>
/// Making, comparing and storing device tokens. One place, so nobody reaches for
/// <c>Guid.NewGuid()</c> or <c>Random</c> - neither is cryptographic (Part 4).
/// </summary>
public static class DeviceTokens
{
    /// <summary>256 bits. Far beyond guessing, and it costs nothing at this length.</summary>
    private const int SecretBytes = 32;

    /// <summary>A fresh token, from the cryptographic generator.</summary>
    public static string Create()
    {
        Span<byte> secret = stackalloc byte[SecretBytes];
        RandomNumberGenerator.Fill(secret);

        return Convert.ToBase64String(secret);
    }

    /// <summary>
    /// Compares in constant time. A plain <c>==</c> returns as soon as two characters differ,
    /// which tells a patient attacker how much of his guess was right.
    /// </summary>
    public static bool Matches(string? presented, string? expected)
    {
        if (presented is null || expected is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>Turns a token into the form that may sit in a configuration file.</summary>
    public static string Store(ISecretStore secrets, string token)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        return Convert.ToBase64String(secrets.Protect(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>
    /// Reads a stored token back. <see langword="false"/> for anything that is not ours -
    /// truncated file, foreign profile, hand-edited nonsense. The caller drops that device and
    /// lets it pair again, which is the whole point of returning a value instead of throwing.
    /// </summary>
    public static bool TryRead(ISecretStore secrets, string? stored, out string token)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        token = string.Empty;

        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        byte[] ciphertext;

        try
        {
            ciphertext = Convert.FromBase64String(stored);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!secrets.TryUnprotect(ciphertext, out var plaintext))
        {
            return false;
        }

        token = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);

        return true;
    }
}

/// <summary>
/// The four digits the DM compares between the table and his screen.
/// <para>
/// It is NOT a secret and does not protect anything - the token does that. It answers one
/// question: is the device asking to be let in the one standing in front of me? Four digits are
/// enough for that, because the DM is looking at both at the same moment.
/// </para>
/// </summary>
public static class PairingCodes
{
    /// <summary>A fresh four digit code, evenly distributed over 0000-9999.</summary>
    public static string Create() =>
        RandomNumberGenerator.GetInt32(0, 10_000).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
}
