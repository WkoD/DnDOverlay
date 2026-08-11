using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests;

/// <summary>
/// Tokens and the vault they sit in. Against a fake rather than against the runner's crypto
/// stack - which is exactly what <see cref="ISecretStore"/> was for: the tests become
/// deterministic and run on both platforms, while DPAPI stays in the application (Part 4).
/// </summary>
public sealed class PairingTests
{
    [Fact]
    public void Two_tokens_are_never_the_same()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => DeviceTokens.Create()).ToList();

        Assert.Equal(100, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token => Assert.True(token.Length >= 40));
    }

    [Fact]
    public void A_token_survives_the_vault()
    {
        var secrets = new Vault();
        var token = DeviceTokens.Create();

        var stored = DeviceTokens.Store(secrets, token);

        Assert.NotEqual(token, stored);
        Assert.True(DeviceTokens.TryRead(secrets, stored, out var read));
        Assert.Equal(token, read);
    }

    /// <summary>
    /// A copied profile, a restored backup, a reinstalled Windows: an ordinary situation with the
    /// same consequence as a missing token - the device pairs again. An exception from the depths
    /// would make a routine event look like a fault (Part 4).
    /// </summary>
    [Fact]
    public void A_token_from_a_foreign_profile_is_absent_rather_than_fatal()
    {
        var stored = DeviceTokens.Store(new Vault(), DeviceTokens.Create());

        Assert.False(DeviceTokens.TryRead(new Vault(refuses: true), stored, out var read));
        Assert.Empty(read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    public void Nonsense_in_the_file_is_absent_too(string stored)
    {
        Assert.False(DeviceTokens.TryRead(new Vault(), stored, out _));
        Assert.False(DeviceTokens.TryRead(new Vault(), null, out _));
    }

    /// <summary>
    /// Compared in constant time. A plain <c>==</c> returns as soon as two characters differ,
    /// which tells a patient attacker how much of his guess was right.
    /// </summary>
    [Fact]
    public void A_token_matches_only_itself()
    {
        var token = DeviceTokens.Create();

        Assert.True(DeviceTokens.Matches(token, token));
        Assert.True(DeviceTokens.Matches(string.Concat(token), token));
        Assert.False(DeviceTokens.Matches(token, DeviceTokens.Create()));
        Assert.False(DeviceTokens.Matches(token[..^1], token));
        Assert.False(DeviceTokens.Matches(null, token));
        Assert.False(DeviceTokens.Matches(token, null));
    }

    /// <summary>
    /// Four digits, always - including the ones with leading zeros. The DM compares them across
    /// the room, so "271" beside "4271" would be one comparison too many (Part 4).
    /// </summary>
    [Fact]
    public void A_pairing_code_is_always_four_digits()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => PairingCodes.Create()).ToList();

        Assert.All(codes, code => Assert.Equal(4, code.Length));
        Assert.All(codes, code => Assert.All(code, digit => Assert.True(char.IsAsciiDigit(digit))));
        Assert.True(codes.Distinct(StringComparer.Ordinal).Count() > 1);
    }

    /// <summary>
    /// A vault that reverses bytes: enough to prove that the caller stores what it was given and
    /// reads back what it stored, without pretending to be encryption.
    /// </summary>
    private sealed class Vault(bool refuses = false) : ISecretStore
    {
        public byte[] Protect(byte[] plaintext) => [.. plaintext.Reverse()];

        public bool TryUnprotect(byte[] ciphertext, out byte[] plaintext)
        {
            if (refuses)
            {
                plaintext = [];
                return false;
            }

            plaintext = [.. ciphertext.Reverse()];

            return true;
        }
    }
}
