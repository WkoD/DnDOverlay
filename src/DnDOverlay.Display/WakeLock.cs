using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DnDOverlay.Display;

/// <summary>
/// Keeps the screens of this machine on while a control is connected.
/// <para>
/// A display PC shows a still picture for an hour at a time, and Windows cannot tell that from an
/// idle machine: the table goes dark in the middle of a scene, and the players are the ones who
/// notice. So the request is held for as long as it is true and dropped the moment it is not -
/// switchable from afar, because the machine it matters on is the one nobody is sitting at
/// (Part 6).
/// </para>
/// </summary>
internal sealed partial class WakeLock : IDisposable
{
    /// <summary>
    /// The flag that makes this a STATE rather than a nudge. Without it the call resets the idle
    /// timer once and the screen goes dark a minute later anyway - which looks exactly like the
    /// bug this is meant to fix, only rarer.
    /// </summary>
    private const uint Continuous = 0x80000000;

    /// <summary>
    /// The display, not the system. What is wanted is a screen that stays lit; asking Windows to
    /// keep the whole machine awake as well would claim more than the reason justifies.
    /// </summary>
    private const uint DisplayRequired = 0x00000002;

    /// <summary>
    /// The thread this was set on OWNS the request, and the request dies with that thread. This is
    /// therefore not a detail of the call site but of the type: everything goes through the
    /// dispatcher, so the request lives on the UI thread - the one thread of this process that is
    /// alive for exactly as long as the application is.
    /// </summary>
    private readonly Dispatcher _dispatcher;

    private readonly Action<bool> _report;

    private bool _connected;
    private bool _wanted = true;
    private bool _held;

    internal WakeLock(Dispatcher dispatcher, Action<bool> report)
    {
        _dispatcher = dispatcher;
        _report = report;
    }

    /// <summary>Whether a control is on the other end right now.</summary>
    internal bool Connected
    {
        set
        {
            _connected = value;
            Update();
        }
    }

    /// <summary>
    /// What the parameter says. Set from <c>display.json</c> at startup and from every
    /// <c>ConfigUpdate</c> after that - the same value either way, which is what keeps the remote
    /// switch and the local one from being two mechanisms (Part 6).
    /// </summary>
    internal bool Wanted
    {
        get => _wanted;

        set
        {
            _wanted = value;
            Update();
        }
    }

    /// <summary>
    /// Lets go on the way out. Windows would drop the request with the thread anyway, but a
    /// process that is merely closing its last window is not a process that has ended.
    /// </summary>
    public void Dispose()
    {
        _connected = false;
        _wanted = false;

        Update();
    }

    private void Update()
    {
        var hold = _connected && _wanted;

        if (hold == _held)
        {
            return;
        }

        _held = hold;

        // Invoke rather than InvokeAsync on purpose: after this returns the state IS what it says,
        // so a Dispose that follows straight away cannot overtake the acquisition it is undoing.
        _ = _dispatcher.Invoke(() => SetThreadExecutionState(hold ? Continuous | DisplayRequired : Continuous));

        _report(hold);
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);
}
