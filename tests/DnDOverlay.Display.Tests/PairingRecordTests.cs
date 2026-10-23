using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Core.Protocol;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// The device's pairing record: the token travels with its control, a Welcome keeps a fresh one, and
/// only a clone's refusal changes anything (Part 4, Part 7).
/// </summary>
public sealed class PairingRecordTests
{
    private static readonly Guid Control = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Device = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Fresh = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_paired_device_carries_its_token()
    {
        var secrets = new Secrets();
        var settings = Paired(secrets);

        Assert.Equal("geheim", PairingRecord.TokenFor(settings, secrets));
    }

    /// <summary>A token without the control it belongs to would be offered to the first hub that answers.</summary>
    [Fact]
    public void A_token_without_its_control_is_not_carried()
    {
        var secrets = new Secrets();
        var settings = Paired(secrets) with { ControlId = null };

        Assert.Null(PairingRecord.TokenFor(settings, secrets));
    }

    /// <summary>A copied profile cannot decrypt the token: it is simply absent, and the device pairs again.</summary>
    [Fact]
    public void A_token_that_does_not_decrypt_is_absent()
    {
        var settings = Paired(new Secrets());

        Assert.Null(PairingRecord.TokenFor(settings, new Secrets(foreign: true)));
        Assert.Null(PairingRecord.TokenFor(settings with { Token = "kein base64!" }, new Secrets()));
        Assert.Null(PairingRecord.TokenFor(new DisplayConfiguration { DeviceId = Device }, new Secrets()));
    }

    /// <summary>A Welcome with a token keeps it encrypted, with the control that issued it - and the next Hello carries it.</summary>
    [Fact]
    public void A_welcome_keeps_the_token_with_its_control()
    {
        var secrets = new Secrets();
        var before = new DisplayConfiguration { DeviceId = Device, DeviceName = "Tisch" };

        var after = PairingRecord.Welcomed(before, new WelcomeMessage(Control, "/assets", "neu"), secrets);

        Assert.NotNull(after);
        Assert.Equal(Control, after.ControlId);
        Assert.NotEqual("neu", after.Token);
        Assert.Equal("neu", PairingRecord.TokenFor(after, secrets));
        Assert.Equal(Device, after.DeviceId);
        Assert.Equal("Tisch", after.DeviceName);
    }

    /// <summary>A Welcome to a device already paired hands out no token, and the record stays as it is.</summary>
    [Fact]
    public void A_welcome_without_a_token_changes_nothing()
    {
        var secrets = new Secrets();

        Assert.Null(PairingRecord.Welcomed(Paired(secrets), new WelcomeMessage(Control, "/assets"), secrets));
    }

    /// <summary>
    /// A cloned disk: the device makes its own fresh identity and drops the binding of the old one -
    /// the rest of the record, its name included, stays (Part 3, Part 7).
    /// </summary>
    [Fact]
    public void A_clone_takes_a_fresh_identity_and_drops_the_binding()
    {
        var settings = Paired(new Secrets()) with { DeviceName = "Tisch", Host = "control" };

        var after = PairingRecord.Rejected(settings, RejectionReason.DuplicateDevice, Fresh);

        Assert.Equal(settings with { DeviceId = Fresh, ControlId = null, Token = null }, after);
    }

    /// <summary>
    /// An unknown token does NOT unbind: the beacon is unauthenticated, and a forged control answering
    /// every Hello with "unknown token" would otherwise unbind every display in the house (Part 4).
    /// No other refusal touches the record either.
    /// </summary>
    [Theory]
    [InlineData(RejectionReason.InvalidToken)]
    [InlineData(RejectionReason.Denied)]
    [InlineData(RejectionReason.LimitExceeded)]
    public void Every_other_refusal_keeps_the_record(RejectionReason reason)
    {
        Assert.Null(PairingRecord.Rejected(Paired(new Secrets()), reason, Fresh));
    }

    private static DisplayConfiguration Paired(Secrets secrets) =>
        new() { DeviceId = Device, ControlId = Control, Token = DeviceTokens.Store(secrets, "geheim") };

    /// <summary>A reversible stand-in for DPAPI; a foreign one cannot read what another wrote.</summary>
    private sealed class Secrets(bool foreign = false) : ISecretStore
    {
        public byte[] Protect(byte[] plaintext) => [.. plaintext.Select(one => (byte)(one ^ 0x5A))];

        public bool TryUnprotect(byte[] ciphertext, out byte[] plaintext)
        {
            plaintext = foreign ? [] : [.. ciphertext.Select(one => (byte)(one ^ 0x5A))];

            return !foreign;
        }
    }
}
