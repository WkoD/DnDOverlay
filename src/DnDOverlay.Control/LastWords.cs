using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Control;

/// <summary>
/// Makes a fault that ends this process say so before it does.
/// <para>
/// It catches nothing and prevents nothing - the run still ends. What it prevents is a run that
/// ends <b>mutely</b>: measured, the control went away with exit code -1 while its log file stopped
/// mid-sentence, and a hand run that had just found a real fault could say nothing about it beyond
/// "it was gone". A crash nobody can read is a crash nobody can fix (Part 1).
/// </para>
/// <para>
/// Three doors, because a fault leaves through whichever one it started behind: the UI thread, any
/// other thread, and a task nobody awaited. The third is the quietest of them - it does not end the
/// process at all since .NET 4.5, so without a line here it is invisible for ever.
/// </para>
/// </summary>
internal static class LastWords
{
    internal static void Listen(Application application, ILogger logger)
    {
        application.DispatcherUnhandledException += (_, e) =>
        {
            ControlLog.UnhandledFault(logger, e.Exception, "the UI thread");

            // Deliberately not handled. Swallowing it would leave a control standing with a broken
            // half of itself, which is worse than an end somebody notices.
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // Built before the call, not inside it: what arrives here is typed as object, and an
            // argument the analyser cannot see through would be built even when nobody is logging.
            var fault = e.ExceptionObject as Exception
                ?? new InvalidOperationException(e.ExceptionObject.ToString());

            ControlLog.UnhandledFault(logger, fault, "a background thread");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ControlLog.UnhandledFault(logger, e.Exception, "a task nobody awaited");

            // Observed, so it does not go on to be re-raised. The line above is the whole point:
            // the process survives this one, and a fault it survives is exactly the sort that
            // otherwise never comes to light.
            e.SetObserved();
        };
    }
}
