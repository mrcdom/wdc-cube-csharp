namespace Wdc.Cube;

/// <summary>What a guarded action needs from whoever binds it (a presenter or the application presenter).</summary>
internal interface IActionHost
{
    CubeApplication Application { get; }

    bool IsReleased { get; }

    /// <summary>Marks the root scope dirty when nothing was marked since the last flush (auto-update).</summary>
    void UpdateIfNotDirty();

    /// <summary>A guarded action of this host failed (the host's <c>OnActionFailure</c>).</summary>
    void ActionFailed(string action, Exception caught);
}

/// <summary>
/// Builds guarded actions: the wrapper marshals to the dispatcher, runs one action at a time per application
/// (<see cref="CubeApplication.RunAction(Func{Task})"/>), hands a failure to the host's <c>OnActionFailure</c> (by
/// default <see cref="CubeApplication.Unexpected"/>; an action never faults to its caller, and every exception is a
/// failure, but for a cancellation once the host is gone), and at the end flushes the scope updates at once and
/// republishes the address.
/// </summary>
internal static class ActionGuard
{
    public static Func<Task> Wrap(IActionHost host, Func<Task> action, string? name) =>
        () => Run(host, action, name);

    private static Task Run(IActionHost host, Func<Task> action, string? name) =>
        host.Application.Dispatcher.InvokeAsync(() => host.Application.RunAction(async () =>
        {
            // Checked after the wait too: the action before this one may have taken the presenter off the face (a
            // second click on "Buy" lands after the first one went to the receipt).
            if (host.IsReleased)
            {
                return; // a late click on a scope whose presenter already left the face
            }

            try
            {
                await action();
            }
            catch (OperationCanceledException) when (host.IsReleased)
            {
                // Cancelled because the presenter left: nobody is there to hear of it. Any other cancellation (an
                // HTTP timeout is one) is a failure like any other.
            }
            catch (Exception caught)
            {
                var action = name ?? "an";
                try
                {
                    host.ActionFailed(action, caught);
                }
                catch (Exception failure)
                {
                    // The handler failed too: the application hears of both.
                    host.Application.Unexpected($"During execution of {action} action", caught);
                    host.Application.Unexpected($"Handling the failure of {action} action", failure);
                }
            }
            finally
            {
                AfterAction(host);
            }
        }));

    private static void AfterAction(IActionHost host)
    {
        var app = host.Application;
        try
        {
            if (!host.IsReleased)
            {
                host.UpdateIfNotDirty();
            }

            app.Updates.FlushNow(); // the user sees the result of an action at once, not a window later

            // A navigation still running publishes the address when it ends; publishing now would write the
            // address of the face being left.
            if (!app.IsFlipping && !app.IsReleased)
            {
                app.UpdateHistory();
            }
        }
        catch (Exception caught)
        {
            app.Unexpected("Updating after an action", caught);
        }
    }
}
