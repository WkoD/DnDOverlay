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
