using System.Windows.Threading;

namespace DnDOverlay.Display.Tests;

/// <summary>
/// A WPF dispatcher on a thread of its own - the UI thread the display's types expect. No window is
/// made on it; it only runs what is posted to it.
/// </summary>
internal sealed class DispatcherThread : IDisposable
{
    private readonly Thread _thread;

    internal DispatcherThread()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;

        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();

        Dispatcher = dispatcher!;
    }

    internal Dispatcher Dispatcher { get; }

    internal int ThreadId => _thread.ManagedThreadId;

    public void Dispose()
    {
        Dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
