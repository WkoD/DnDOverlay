using System.IO;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Control.Tests;

/// <summary>
/// The one owner of control.json in this process (Part 6): several callers, one writer - a change is
/// applied to what is current, so no caller's save drops another's.
/// </summary>
public sealed class ControlSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ControlSettingsTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// Pairing and the screen inventory change different fields one after the other: both stay. A
    /// change made to a copy read earlier would have dropped the first - the device paired again after
    /// a screen was renamed.
    /// </summary>
    [Fact]
    public void Two_callers_changing_different_fields_both_keep_their_change()
    {
        using var file = File();
        var settings = new ControlSettings(file, new ControlConfiguration());
        var readEarly = settings.Current;

        settings.Update(current => current with { Port = 50123 });
        settings.Update(current => current with { CountedNameNext = readEarly.CountedNameNext + 4 });

        Assert.Equal(50123, settings.Current.Port);
        Assert.Equal(5, settings.Current.CountedNameNext);
    }

    /// <summary>The write past the debounce, for the change whose loss would cost a pairing (Part 6): read back as a start reads it.</summary>
    [Fact]
    public void Flush_puts_the_current_document_on_disk()
    {
        using var file = File();
        var settings = new ControlSettings(file, new ControlConfiguration());

        settings.Update(current => current with { CampaignsPath = @"E:\Kampagnen" });
        settings.Flush();

        using var again = File();

        Assert.Equal(@"E:\Kampagnen", again.Load(() => new ControlConfiguration()).Value.CampaignsPath);
    }

    /// <summary>What the process started with is what it reports until something changes it.</summary>
    [Fact]
    public void The_document_handed_in_is_current_until_changed()
    {
        using var file = File();
        var start = new ControlConfiguration { Port = 50999 };
        var settings = new ControlSettings(file, start);

        Assert.Same(start, settings.Current);
    }

    private ConfigurationFile<ControlConfiguration> File() =>
        new(Path.Combine(_folder, "control.json"), ConfigurationJsonContext.Default.ControlConfiguration, TimeProvider.System);
}
