namespace DnDOverlay.Display.Tests;

/// <summary>One message that fails costs that message, not the connection (Part 1).</summary>
public sealed class MessageGuardTests
{
    [Fact]
    public async Task A_failing_handler_is_said_and_the_loop_reads_on()
    {
        Exception? said = null;
        var fault = new InvalidOperationException("Collection was modified");

        await MessageGuard.HandleAsync(() => throw fault, caught => said = caught, CancellationToken.None);

        Assert.Same(fault, said);
    }

    [Fact]
    public async Task A_fault_after_an_await_is_caught_as_well()
    {
        Exception? said = null;

        await MessageGuard.HandleAsync(
            async () =>
            {
                await Task.Yield();
                throw new System.ComponentModel.Win32Exception(2);
            },
            caught => said = caught,
            CancellationToken.None);

        Assert.IsType<System.ComponentModel.Win32Exception>(said);
    }

    /// <summary>A cancellation that is not the end of the application is a fault like any other.</summary>
    [Fact]
    public async Task A_stray_cancellation_is_a_fault_too()
    {
        Exception? said = null;

        await MessageGuard.HandleAsync(() => throw new TaskCanceledException(), caught => said = caught, CancellationToken.None);

        Assert.IsType<TaskCanceledException>(said);
    }

    [Fact]
    public async Task The_end_of_the_application_passes_through()
    {
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        var said = false;

        await Assert.ThrowsAsync<OperationCanceledException>(() => MessageGuard.HandleAsync(
            () => throw new OperationCanceledException(shutdown.Token),
            _ => said = true,
            shutdown.Token));

        Assert.False(said);
    }
}
