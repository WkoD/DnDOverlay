namespace DnDOverlay.Display;

/// <summary>
/// Handles one message from the hub so that its failure stays its own.
/// <para>
/// <b>Every handler runs on the connection task</b>, and whatever it throws - a window that cannot be
/// made, a restart whose executable is locked mid-update, a table read while another thread changes
/// it - used to leave the receive loop, then the connection, then the loop that looks for a control.
/// From then on the device never connected again, on a machine nobody is sitting at (Part 1). One
/// bad message costs that message now: it is said, and the next one is read.
/// </para>
/// <para>
/// Only the end of the application passes through - that is the loop's own way out, not a fault.
/// </para>
/// </summary>
internal static class MessageGuard
{
    internal static async Task HandleAsync(Func<Task> handle, Action<Exception> failed, CancellationToken shutdown)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(failed);

        try
        {
            await handle().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any fault of one message, by design: the connection must outlive it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            failed(fault);
        }
    }
}
