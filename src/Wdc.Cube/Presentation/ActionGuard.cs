namespace Wdc.Cube;

/// <summary>What a guarded action needs from whoever binds it (a presenter or the application presenter).</summary>
internal interface IActionHost
{
    CubeApplication Application { get; }

    bool IsReleased { get; }

    /// <summary>Marks the root scope dirty when nothing was marked since the last flush (auto-update).</summary>
    void UpdateIfNotDirty();
}

/// <summary>
/// Builds guarded actions: the wrapper marshals to the dispatcher, reports failures through
/// <see cref="CubeApplication.Unexpected"/> (an action never faults to its caller), and at the end flushes the scope
/// updates at once and republishes the address.
/// </summary>
internal static class ActionGuard
{
    public static Func<Task> Wrap(IActionHost host, Func<Task> action, string? name) =>
        () => Run(host, action, name);

    private static Task Run(IActionHost host, Func<Task> action, string? name) =>
        host.Application.Dispatcher.InvokeAsync(async () =>
        {
            if (host.IsReleased)
            {
                return; // a late click on a scope whose presenter already left the face
            }

            try
            {
                await action();
            }
            catch (Exception caught)
            {
                host.Application.Unexpected($"During execution of {name ?? "an"} action", caught);
            }
            finally
            {
                AfterAction(host);
            }
        });

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
