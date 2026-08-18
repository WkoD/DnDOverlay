using DnDOverlay.Core;
using DnDOverlay.Core.Configuration;

namespace DnDOverlay.Hub;

/// <summary>
/// What the hub knows about the screens out there: the facts a device reported, the DM's wish,
/// the display parameters every computation over a scene needs, and what is getting in the way
/// right now.
/// <para>
/// All of it is kept even while a device is gone, and that is the point rather than a nicety: a
/// screen is fully playable in every state - expressly including while its device is switched
/// OFF. Were size, DPI and the parameters only ever to arrive in the <c>Hello</c>, the hub could
/// neither place nor cap for an absent device, and preparing the next scene ahead would fall
/// away (Part 3). It is persisted into control.json for the same reason.
/// </para>
/// <para>
/// One lock rather than concurrent dictionaries, because the invariants span several maps: what a
/// device currently reports decides availability, and availability plus the wish make the view.
/// Nothing awaits inside it.
/// </para>
/// </summary>
public sealed class ScreenCatalog
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ScreenRef, Entry> _entries = [];
    private readonly Dictionary<DeviceId, Presence> _present = [];
    private readonly Dictionary<ScreenRef, ScreenSettings> _pending = [];
    private readonly Dictionary<DeviceId, DeviceSettings> _pendingDevice = [];

    private long _reports;

    /// <summary>
    /// Fires when something worth <b>keeping</b> has changed - a screen appeared, a wish was set, a
    /// parameter moved.
    /// <para>
    /// The control writes control.json from it. Polling would be the obvious alternative and is
    /// quietly broken here: the configuration file debounces, so a save on every tick would push
    /// its own deadline out for ever and write nothing at all. Findings are deliberately NOT in
    /// here - they are never persisted (Part 3).
    /// </para>
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Fires when something worth <b>showing</b> has changed - everything <see cref="Changed"/>
    /// covers, and the findings besides.
    /// <para>
    /// Two events rather than one, because the two questions are genuinely different: what is worth
    /// writing to disk, and what is worth putting in front of the DM. A finding belongs to the
    /// second and never to the first - it is transient by definition, and a screen that is
    /// unavailable has to say so on the tile without that ever reaching control.json (Part 3).
    /// </para>
    /// </summary>
    public event Action? ViewChanged;

    /// <summary>Every screen the hub has ever been told about, connected or not.</summary>
    public IReadOnlyCollection<ScreenRef> Known
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Keys];
            }
        }
    }

    /// <summary>
    /// Takes what a display said about itself, and says what changed about its inventory.
    /// <para>
    /// The settings that come with it are the BASELINE of the two-sided configuration: the
    /// control takes over what the device reports, and only afterwards sends what it changed
    /// while the device was away. Per key the value set last therefore holds, and nobody overruns
    /// something they never touched (Part 4).
    /// </para>
    /// </summary>
    public InventoryChange Report(DeviceId device, IReadOnlyList<ScreenInfo> screens, ConfigUpdate? reported)
    {
        ArgumentNullException.ThrowIfNull(screens);

        var added = new List<ScreenRef>();
        var changed = new List<ScreenRef>();
        InventoryChange result;

        lock (_gate)
        {
            var settings = Settings(reported);

            foreach (var screen in screens)
            {
                var key = new ScreenRef(device, screen.ScreenId);

                if (!_entries.TryGetValue(key, out var entry))
                {
                    // A screen nobody has met becomes Enabled, like any unknown one (Part 3).
                    var fresh = ScreenContext.Default(screen.Size, screen.Dpi);

                    _entries[key] = new Entry(
                        screen,
                        ScreenState.Enabled,
                        Baseline(fresh, settings, screen.ScreenId));

                    added.Add(key);
                    continue;
                }

                // Size and DPI are hardware facts and always win. A different resolution, aspect
                // ratio or DPI is the one finding at which something actually breaks: clamping and
                // capping are recomputed, items move, and undo does not reach transformations
                // (Part 3). Hence loud, and hence told apart from the harmless two.
                if (entry.Info.Size != screen.Size || entry.Info.Dpi != screen.Dpi)
                {
                    changed.Add(key);
                }

                // First take over what the device reports, THEN lay our own outstanding change
                // back on top - that order is the whole reconciliation. Without the second half
                // the baseline would undo exactly what the control set while the device was away,
                // and per key the value set last would no longer hold (Part 4).
                var taken = Baseline(entry.Context, settings, screen.ScreenId);

                if (_pending.TryGetValue(key, out var mine))
                {
                    taken = mine.ApplyTo(taken);
                }

                _entries[key] = entry with
                {
                    Info = screen,
                    Context = taken with { Size = screen.Size, Dpi = screen.Dpi },
                };
            }

            var now = screens.Select(screen => screen.ScreenId).ToHashSet();
            var before = _present.TryGetValue(device, out var seen) ? seen.Screens : [];

            var presence = ++_reports;

            _present[device] = new Presence(presence, now);

            // Missing is a plain fact and expressly carries no loss warning: the tile stays, the
            // scene stays, and "save screen as scene" goes on working. A warning about a loss that
            // is not happening would make the other two messages untrustworthy (Part 3).
            var missing = _entries.Keys
                .Where(key => key.Device == device && !now.Contains(key.Screen) && before.Contains(key.Screen))
                .ToList();

            // The device settings the device reported become ours, then whatever we changed while
            // it was away goes on top.
            if (reported?.Device is { } device_ && !device_.IsEmpty)
            {
                _pendingDevice[device] = _pendingDevice.TryGetValue(device, out var mine)
                    ? DeviceSettings.Merge(older: device_, newer: mine)
                    : device_;
            }

            result = new InventoryChange(added, missing, changed, presence);
        }

        // Always, not only when something was added: sizes, DPI and the reported baseline can all
        // have moved without a screen appearing or going.
        Announce(persist: true);

        return result;
    }

    /// <summary>
    /// The device is gone. Its screens stay known and stay settable - only their availability
    /// ends, and that is a finding rather than a state (Part 3).
    /// </summary>
    /// <param name="presence">
    /// The ticket <see cref="Report"/> gave this connection. A stale one is ignored, which is what
    /// keeps a slow departure from switching off a table that is already back.
    /// </param>
    public void Departed(DeviceId device, long presence)
    {
        var present = false;

        lock (_gate)
        {
            if (_present.TryGetValue(device, out var seen) && seen.Ticket == presence)
            {
                present = _present.Remove(device);
            }
        }

        if (present)
        {
            // Worth showing, never worth writing: every screen of this device has just become
            // unavailable, and the tiles have to say so - while control.json keeps exactly the
            // wishes it kept before (Part 3).
            Announce(persist: false);
        }
    }

    /// <summary>
    /// The context to compute with. An unknown screen gets the defaults rather than an
    /// exception: the hub must be able to prepare a scene for a screen it has not met yet.
    /// </summary>
    public ScreenContext ContextFor(ScreenRef screen)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(screen, out var entry)
                ? entry.Context
                : ScreenContext.Default(new PixelSize(1920, 1080), 96);
        }
    }

    public ScreenInfo? InfoFor(ScreenRef screen)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(screen, out var entry) ? entry.Info : null;
        }
    }

    /// <summary>
    /// Reported facts, wish and finding in one - the shape the control shows. This is the only
    /// place the two halves are put together (Part 3).
    /// </summary>
    public ScreenView? ViewOf(ScreenRef screen)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(screen, out var entry) ? View(screen, entry) : null;
        }
    }

    /// <summary>Every known screen, as the control shows it.</summary>
    public IReadOnlyList<ScreenView> Views()
    {
        lock (_gate)
        {
            return [.. _entries.Select(pair => View(pair.Key, pair.Value))];
        }
    }

    /// <summary>
    /// Sets the DM's wish. Returns <see langword="false"/> when it already held, so nothing is
    /// sent and nothing is written for a change that is not one.
    /// </summary>
    public bool SetState(ScreenRef screen, ScreenState state)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(screen, out var entry) || entry.State == state)
            {
                return false;
            }

            _entries[screen] = entry with { State = state };
        }

        Announce(persist: true);

        return true;
    }

    /// <summary>
    /// Sets or clears a finding. It leaves <see cref="ScreenState"/> untouched - which is what
    /// makes the return trip free: there is nothing to restore, the screen is simply played on
    /// again with the wish that stood there all along (Part 3).
    /// </summary>
    public bool SetSuppress(ScreenRef screen, SuppressReason? reason)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(screen, out var entry) || entry.Suppress == reason)
            {
                return false;
            }

            _entries[screen] = entry with { Suppress = reason };
        }

        Announce(persist: false);

        return true;
    }

    /// <summary>
    /// A change from the control's side. It is remembered per screen until it has gone out, so a
    /// screen set while its device was switched off is not lost - that is the whole reason the
    /// device window works without the device (Part 7).
    /// </summary>
    public void Change(ScreenRef screen, ScreenSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(screen, out var entry))
            {
                _entries[screen] = entry with { Context = settings.ApplyTo(entry.Context) };
            }

            _pending[screen] = _pending.TryGetValue(screen, out var known)
                ? ScreenSettings.Merge(older: known, newer: settings)
                : settings;
        }

        Announce(persist: true);
    }

    /// <summary>A device-scope change from the control's side.</summary>
    public void Change(DeviceId device, DeviceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            _pendingDevice[device] = _pendingDevice.TryGetValue(device, out var known)
                ? DeviceSettings.Merge(older: known, newer: settings)
                : settings;
        }

        Announce(persist: true);
    }

    /// <summary>
    /// Takes a delta a DEVICE sent. Settings only: a screen wish and a finding are the control's
    /// alone, and one that arrives from a device is passed over. The caller logs that - the
    /// catalogue says what happened, it does not decide what it costs.
    /// </summary>
    public bool Apply(DeviceId device, ConfigUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var refused = false;
        var applied = false;

        lock (_gate)
        {
            foreach (var screen in update.Screens)
            {
                refused |= screen.Command is not null;

                if (screen.Settings is not { IsEmpty: false } settings)
                {
                    continue;
                }

                var key = new ScreenRef(device, screen.Screen);

                if (_entries.TryGetValue(key, out var entry))
                {
                    _entries[key] = entry with { Context = settings.ApplyTo(entry.Context) };
                    applied = true;
                }
            }
        }

        if (applied)
        {
            Announce(persist: true);
        }

        return refused;
    }

    /// <summary>
    /// What a freshly connected device has to be told: how each of its screens stands, plus
    /// whatever the control changed while it was away. Clears the pending changes, because they
    /// are on their way out.
    /// </summary>
    public ConfigUpdate Drain(DeviceId device)
    {
        lock (_gate)
        {
            var screens = new List<ScreenConfigUpdate>();

            foreach (var (key, entry) in _entries.Where(pair => pair.Key.Device == device))
            {
                _pending.Remove(key, out var settings);

                screens.Add(new ScreenConfigUpdate(
                    key.Screen,
                    settings is { IsEmpty: false } ? settings : null,
                    // The finding travels, the availability does not: a device that is connected
                    // has by definition reported these screens, and one that is not gets nothing
                    // at all. Only the control window can suppress a screen that is right here.
                    new ScreenCommand(entry.State, entry.Suppress)));
            }

            _pendingDevice.Remove(device, out var pending);

            return new ConfigUpdate(screens, pending is { IsEmpty: false } ? pending : null);
        }
    }

    /// <summary>What goes into control.json.</summary>
    public IReadOnlyList<KnownScreen> Snapshot()
    {
        lock (_gate)
        {
            return
            [
                .. _entries.Select(pair => new KnownScreen(
                    pair.Key.Device.Value,
                    pair.Key.Screen.Value,
                    pair.Value.Info.Label,
                    pair.Value.State,
                    pair.Value.Info.Size,
                    pair.Value.Info.Dpi,
                    ScreenSettings.Of(pair.Value.Context, pair.Value.Info.CustomName))),
            ];
        }
    }

    /// <summary>
    /// What comes back out of control.json at startup. Nothing is present yet - every screen is
    /// known and none is available until a device says so.
    /// </summary>
    public void Restore(IReadOnlyList<KnownScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        lock (_gate)
        {
            foreach (var screen in screens)
            {
                var key = new ScreenRef(new DeviceId(screen.DeviceId), new ScreenId(screen.ScreenId));

                var info = new ScreenInfo(
                    key.Screen,
                    screen.Label,
                    screen.Settings.CustomName,
                    screen.Size,
                    screen.Dpi,
                    IsPrimary: false);

                _entries[key] = new Entry(
                    info,
                    screen.State,
                    screen.Settings.ApplyTo(ScreenContext.Default(screen.Size, screen.Dpi)));
            }
        }
    }

    /// <summary>
    /// Says what moved, always outside the lock - a subscriber reads straight back, and reading
    /// from inside would mean waiting on the lock that is announcing the change.
    /// </summary>
    /// <param name="persist">
    /// Whether this is also worth writing. Everything is worth showing; only some of it is worth
    /// keeping, and a finding is never (Part 3).
    /// </param>
    private void Announce(bool persist)
    {
        if (persist)
        {
            Changed?.Invoke();
        }

        ViewChanged?.Invoke();
    }

    /// <summary>
    /// Lays a reported baseline over a context. Called under the lock.
    /// </summary>
    private static ScreenContext Baseline(ScreenContext context, Dictionary<ScreenId, ScreenSettings> reported, ScreenId screen) =>
        reported.TryGetValue(screen, out var settings) ? settings.ApplyTo(context) : context;

    private static Dictionary<ScreenId, ScreenSettings> Settings(ConfigUpdate? update) =>
        update is null
            ? []
            : update.Screens
                .Where(screen => screen.Settings is not null)
                .ToDictionary(screen => screen.Screen, screen => screen.Settings!);

    /// <summary>Called under the lock.</summary>
    private ScreenView View(ScreenRef screen, Entry entry) =>
        new(
            screen,
            entry.Info,
            entry.State,
            entry.Suppress ?? (Available(screen) ? null : SuppressReason.Unavailable));

    /// <summary>Called under the lock.</summary>
    private bool Available(ScreenRef screen) =>
        _present.TryGetValue(screen.Device, out var present) && present.Screens.Contains(screen.Screen);

    private sealed record Entry(
        ScreenInfo Info,
        ScreenState State,
        ScreenContext Context,
        SuppressReason? Suppress = null);

    /// <summary>What one device last reported, and which report it was.</summary>
    private sealed record Presence(long Ticket, HashSet<ScreenId> Screens);
}

/// <summary>
/// What a reported inventory changed about what the hub knew. Three findings, and only one of
/// them is dangerous - which is why they are told apart here rather than at the call site
/// (Part 3).
/// </summary>
/// <param name="Changed">
/// Different resolution, aspect ratio or DPI. The only one at which something actually breaks,
/// and therefore the only one that may be loud.
/// </param>
/// <param name="Presence">
/// A ticket for this report. The caller hands it back to <see cref="ScreenCatalog.Departed"/>, so
/// that a handler tidying up late cannot clear the presence a NEWER connection has just
/// established.
/// <para>
/// It is the same cure <see cref="DisplayConnections.Remove"/> uses one floor up - identity rather
/// than key - and this is the other place that needed it: a display that crashed and came straight
/// back is served by a new handler while the old one is still unwinding, and clearing BY DEVICE
/// would mark the live table's screens unavailable.
/// </para>
/// </param>
public sealed record InventoryChange(
    IReadOnlyList<ScreenRef> Added,
    IReadOnlyList<ScreenRef> Missing,
    IReadOnlyList<ScreenRef> Changed,
    long Presence);
