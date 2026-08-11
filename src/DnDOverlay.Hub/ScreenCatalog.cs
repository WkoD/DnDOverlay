using System.Collections.Concurrent;
using DnDOverlay.Core;

namespace DnDOverlay.Hub;

/// <summary>
/// What the hub knows about the screens out there: their reported facts and the
/// <see cref="ScreenContext"/> every computation over a scene needs.
/// <para>
/// The context is kept even while a device is gone, and that is the point rather than a
/// nicety: a screen is fully playable in every state - expressly including while its device is
/// switched OFF. Were size and DPI only ever to arrive in the <c>Hello</c>, the hub could
/// neither place nor cap for an absent device, and preparing the next scene ahead would fall
/// away (Part 3).
/// </para>
/// <para>
/// From M1b this is persisted into control.json. In M1a it lives for the run - which is why a
/// screen unknown to the hub falls back to the defaults from Part 6 rather than refusing.
/// </para>
/// </summary>
public sealed class ScreenCatalog
{
    private readonly ConcurrentDictionary<ScreenRef, ScreenContext> _contexts = new();
    private readonly ConcurrentDictionary<ScreenRef, ScreenInfo> _reported = new();

    /// <summary>Takes what a display said about itself in its <c>Hello</c>.</summary>
    public void Report(DeviceId device, IReadOnlyList<ScreenInfo> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        foreach (var screen in screens)
        {
            var key = new ScreenRef(device, screen.ScreenId);

            _reported[key] = screen;

            // Size and DPI are hardware facts and always win; the display parameters keep
            // whatever was set for this screen before, so a reconnect does not reset them.
            _contexts.AddOrUpdate(
                key,
                _ => ScreenContext.Default(screen.Size, screen.Dpi),
                (_, existing) => existing with { Size = screen.Size, Dpi = screen.Dpi });
        }
    }

    /// <summary>
    /// The context to compute with. An unknown screen gets the defaults rather than an
    /// exception: the hub must be able to prepare a scene for a screen it has not met yet.
    /// </summary>
    public ScreenContext ContextFor(ScreenRef screen) =>
        _contexts.TryGetValue(screen, out var context)
            ? context
            : ScreenContext.Default(new PixelSize(1920, 1080), 96);

    public ScreenInfo? InfoFor(ScreenRef screen) =>
        _reported.TryGetValue(screen, out var info) ? info : null;

    /// <summary>Every screen the hub has ever been told about, connected or not.</summary>
    public IReadOnlyCollection<ScreenRef> Known => _contexts.Keys.ToList();
}
