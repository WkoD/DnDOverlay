using System.IO;
using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;
using DnDOverlay.Hub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// Where a pairing decision meets control.json: an unpair has to survive a restart, and a device
/// whose name is taken - a cloned display PC - is paired under a free one (Part 3, Part 7).
/// </summary>
public sealed class PairingDeskTests : IDisposable
{
    private static readonly DeviceId Table = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e1"));
    private static readonly DeviceId Clone = new(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e2"));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PairingDeskTests-" + Guid.NewGuid().ToString("N"));
    private readonly PlainSecrets _secrets = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// The entry leaves control.json, so the next start does not hand the token back to the hub and
    /// pair the device again without a question - read back from the file, as a start reads it.
    /// </summary>
    [Fact]
    public async Task An_unpaired_device_is_forgotten_in_control_json_and_not_restored_at_the_next_start()
    {
        using var file = Configuration();
        var settings = new ControlSettings(file, new ControlConfiguration
        {
            KnownDevices = [new KnownDevice(Table.Value, "TISCH-PC", PairingRole.Display, DeviceTokens.Store(_secrets, DeviceTokens.Create()), DateTimeOffset.UnixEpoch)],
        });
        var desk = new PairingDesk(Session(), _secrets, settings, TimeProvider.System);

        await desk.UnpairAsync(Table);

        var reread = file.Load(() => new ControlConfiguration()).Value;

        Assert.Empty(settings.Current.KnownDevices);
        Assert.Empty(PairingDesk.Restore(reread, _secrets).Devices);
    }

    [Fact]
    public void A_clone_with_the_originals_name_is_offered_a_free_one()
    {
        IReadOnlyList<DeviceView> devices = [Device(Table, "TISCH-PC", "TISCH-PC//1"), Device(Clone, "TISCH-PC", "TISCH-PC//1")];

        var taken = PairingDesk.TakenNames(devices, Clone);

        Assert.Contains("TISCH-PC", taken);
        Assert.Equal("TISCH-PC (2)", PairingDesk.FreeName("TISCH-PC", taken));
    }

    /// <summary>A screen label is a name too - a device may not be called like another device's screen (Part 3).</summary>
    [Fact]
    public void A_screen_label_of_another_device_is_taken_and_the_own_screens_are_not()
    {
        IReadOnlyList<DeviceView> devices = [Device(Table, "TISCH-PC", "Wand"), Device(Clone, "BEAMER", "BEAMER//1")];

        var taken = PairingDesk.TakenNames(devices, Clone);

        Assert.Contains("Wand", taken);
        Assert.DoesNotContain("BEAMER//1", taken);
        Assert.DoesNotContain("BEAMER", taken);
    }

    [Fact]
    public async Task Allowed_under_a_free_name_that_name_is_kept()
    {
        using var file = Configuration();
        var settings = new ControlSettings(file, new ControlConfiguration());
        var desk = new PairingDesk(Session(), _secrets, settings, TimeProvider.System);

        await desk.AllowAsync(new PendingPairing(Clone, "TISCH-PC", "1234", "10.0.0.2", DateTimeOffset.UnixEpoch, IsClone: false), "TISCH-PC (2)");

        Assert.Equal("TISCH-PC (2)", Assert.Single(settings.Current.KnownDevices).Name);
    }

    /// <summary>
    /// A token that cannot be decrypted - a restored backup, a reinstalled Windows - is dropped and
    /// counted, never fatal: the device pairs again (Part 4). The readable ones come back.
    /// </summary>
    [Fact]
    public void An_unreadable_token_is_dropped_and_counted_and_the_rest_come_back()
    {
        var here = new ProfileSecrets(1);
        var configuration = new ControlConfiguration
        {
            KnownDevices =
            [
                new KnownDevice(Table.Value, "TISCH-PC", PairingRole.Display, DeviceTokens.Store(here, DeviceTokens.Create()), DateTimeOffset.UnixEpoch),
                new KnownDevice(Clone.Value, "BEAMER", PairingRole.Display, DeviceTokens.Store(new ProfileSecrets(2), DeviceTokens.Create()), DateTimeOffset.UnixEpoch),
            ],
        };

        var restored = PairingDesk.Restore(configuration, here);

        Assert.Equal(1, restored.Dropped);
        Assert.Equal(Table, Assert.Single(restored.Devices).Device);
    }

    /// <summary>A device paired a second time - after a reset at the device - has one entry, the new one.</summary>
    [Fact]
    public async Task Pairing_a_known_device_again_replaces_its_entry()
    {
        using var file = Configuration();
        var settings = new ControlSettings(file, new ControlConfiguration
        {
            KnownDevices = [new KnownDevice(Table.Value, "ALT", PairingRole.Display, DeviceTokens.Store(_secrets, DeviceTokens.Create()), DateTimeOffset.UnixEpoch)],
        });
        var desk = new PairingDesk(Session(), _secrets, settings, TimeProvider.System);

        await desk.AllowAsync(new PendingPairing(Table, "TISCH-PC", "1234", "10.0.0.2", DateTimeOffset.UnixEpoch, IsClone: false));

        Assert.Equal("TISCH-PC", Assert.Single(settings.Current.KnownDevices).Name);
    }

    /// <summary>A name of nothing but spaces is no name: the device keeps its own (Part 3).</summary>
    [Fact]
    public async Task Allowed_under_a_blank_name_the_devices_own_name_is_kept()
    {
        using var file = Configuration();
        var settings = new ControlSettings(file, new ControlConfiguration());
        var desk = new PairingDesk(Session(), _secrets, settings, TimeProvider.System);

        await desk.AllowAsync(new PendingPairing(Clone, "TISCH-PC", "1234", "10.0.0.2", DateTimeOffset.UnixEpoch, IsClone: false), "   ");

        Assert.Equal("TISCH-PC", Assert.Single(settings.Current.KnownDevices).Name);
    }

    /// <summary>
    /// The take-over writes first and asks the hub second; refused there, control.json goes back to
    /// what it was - the known entry, its tile place and its screens untouched, and no token for the
    /// device that knocked (Part 7).
    /// </summary>
    [Fact]
    public async Task A_take_over_the_hub_refuses_leaves_control_json_as_it_was()
    {
        using var file = Configuration();
        var screen = new KnownScreen(Table.Value, "DISPLAY1", "Wand", ScreenState.Enabled, new PixelSize(1920, 1080), 96, new ScreenSettings(), ViewRotation.Quarter);
        var before = new ControlConfiguration
        {
            KnownDevices = [new KnownDevice(Table.Value, "TISCH-PC", PairingRole.Display, DeviceTokens.Store(_secrets, DeviceTokens.Create()), DateTimeOffset.UnixEpoch)],
            TileOrder = [new ScreenKey(Table.Value, "DISPLAY1")],
            KnownScreens = [screen],
        };
        var settings = new ControlSettings(file, before);
        var desk = new PairingDesk(Session(), _secrets, settings, TimeProvider.System);

        var adopted = await desk.AdoptAsync(
            new PendingPairing(Clone, "TISCH-PC", "1234", "10.0.0.2", DateTimeOffset.UnixEpoch, IsClone: false),
            Device(Table, "TISCH-PC", "Wand"));

        Assert.False(adopted);
        Assert.Same(before, settings.Current);

        var reread = file.Load(() => new ControlConfiguration()).Value;

        Assert.Equal(Table.Value, Assert.Single(reread.KnownDevices).DeviceId);
        Assert.Equal([new ScreenKey(Table.Value, "DISPLAY1")], reread.TileOrder);
        Assert.Equal(Table.Value, Assert.Single(reread.KnownScreens).DeviceId);
    }

    private ConfigurationFile<ControlConfiguration> Configuration() =>
        new(Path.Combine(_folder, "control.json"), ConfigurationJsonContext.Default.ControlConfiguration, TimeProvider.System);

    private static DeviceView Device(DeviceId device, string name, string label) =>
        new(device, name, Connected: true, [new ScreenView(new ScreenRef(device, new ScreenId("DISPLAY1")), new ScreenInfo(new ScreenId("DISPLAY1"), label, null, new PixelSize(1920, 1080), 96, true), ScreenState.Enabled)]);

    private static SessionApi Session() =>
        new(
            new SceneStore(),
            new ScreenCatalog(),
            new DisplayConnections(),
            new PairingDirectory(Options.Create(new HubOptions()), TimeProvider.System),
            new SessionEvents(),
            null,
            NullLogger<SessionApi>.Instance);

    /// <summary>
    /// A secret store bound to one profile: it marks what it protects and reads only what carries its
    /// own mark - a token written under another profile is unreadable, as DPAPI makes it.
    /// </summary>
    private sealed class ProfileSecrets(byte profile) : ISecretStore
    {
        public byte[] Protect(byte[] plaintext) => [profile, .. plaintext];

        public bool TryUnprotect(byte[] ciphertext, out byte[] plaintext)
        {
            plaintext = ciphertext.Length > 0 ? ciphertext[1..] : [];
            return ciphertext.Length > 0 && ciphertext[0] == profile;
        }
    }

    /// <summary>A secret store that keeps the bytes as they are - DPAPI is not what is under test.</summary>
    private sealed class PlainSecrets : ISecretStore
    {
        public byte[] Protect(byte[] plaintext) => plaintext;

        public bool TryUnprotect(byte[] ciphertext, out byte[] plaintext)
        {
            plaintext = ciphertext;
            return true;
        }
    }
}
