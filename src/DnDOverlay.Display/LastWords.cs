using System.Windows;
using Microsoft.Extensions.Logging;

namespace DnDOverlay.Display;

/// <summary>
/// Makes a fault that ends this process say so before it does - the display's half of what the
/// control does in its own <c>LastWords</c>.
/// <para>
/// It matters more here than there, and for the reason everything about this application is
/// decided: <b>nobody is sitting in front of a display PC.</b> A control that dies is noticed
/// within a second; a display that dies is noticed at the table, by the players, as a screen that
/// went dark for no reason anybody can name.
/// </para>
/// </summary>
internal static class LastWords
{
    internal static void Listen(Application application, ILogger logger)
    {
        application.DispatcherUnhandledException += (_, e) =>
        {
            DisplayLog.UnhandledFault(logger, e.Exception, "the UI thread");
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // Built before the call, not inside it: what arrives here is typed as object, and an
            // argument the analyser cannot see through would be built even when nobody is logging.
            var fault = e.ExceptionObject as Exception
                ?? new InvalidOperationException(e.ExceptionObject.ToString());

            DisplayLog.UnhandledFault(logger, fault, "a background thread");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DisplayLog.UnhandledFault(logger, e.Exception, "a task nobody awaited");
            e.SetObserved();
        };
    }
}
