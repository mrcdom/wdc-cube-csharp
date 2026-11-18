namespace Wdc.Cube;

/// <summary>
/// Runs the presentation code of one application one item at a time (decision D-01). Actions, navigation and
/// scope flushes run here, and every <c>await</c> inside them comes back here, so presenters are written as if the
/// application had a single thread, as in the TypeScript and Kotlin versions.
/// </summary>
/// <remarks>
/// <para>
/// Entry points that return a <see cref="Task"/> marshal themselves (<see cref="InvokeAsync(Func{Task})"/>).
/// Synchronous members that change state call <see cref="VerifyAccess"/> and fail loudly when called from outside.
/// </para>
/// <para>
/// Presentation code must not use <c>ConfigureAwait(false)</c>: it would continue outside the dispatcher.
/// </para>
/// </remarks>
public abstract class PresentationDispatcher
{
    /// <summary>Raised with the exception of a posted item that nobody awaited.</summary>
    public event Action<Exception>? UnhandledException;

    /// <summary>Whether the caller is running on this dispatcher.</summary>
    public abstract bool CheckAccess();

    /// <summary>Queues <paramref name="callback"/> to run on this dispatcher, after what is already queued.</summary>
    public abstract void Post(SendOrPostCallback callback, object? state);

    /// <summary>
    /// A dispatcher over the host's own serial context (Blazor's renderer dispatcher, a UI thread). The host's
    /// synchronization context brings every <c>await</c> back, so the adapter only needs to post and to answer
    /// whether the caller is on it.
    /// </summary>
    /// <param name="post">Runs an action on the host's context, later.</param>
    /// <param name="checkAccess">Whether the caller is on the host's context.</param>
    public static PresentationDispatcher FromHost(Action<Action> post, Func<bool> checkAccess) =>
        new HostDispatcher(post, checkAccess);

    /// <summary>Queues <paramref name="action"/> to run on this dispatcher.</summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Post(static state => ((Action)state!)(), action);
    }

    /// <summary>Throws when the caller is not on this dispatcher.</summary>
    /// <exception cref="InvalidOperationException">The caller is not on this dispatcher.</exception>
    public void VerifyAccess()
    {
        if (!CheckAccess())
        {
            throw new InvalidOperationException(
                "This member must run on the application's presentation dispatcher. " +
                "Enter it through app.InvokeAsync(...), and do not use ConfigureAwait(false) in presentation code.");
        }
    }

    /// <summary>Runs <paramref name="action"/> on this dispatcher: inline when already on it, queued otherwise.</summary>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return InvokeAsync(() =>
        {
            action();
            return true;
        });
    }

    /// <summary>Runs <paramref name="func"/> on this dispatcher and returns its result.</summary>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (CheckAccess())
        {
            try
            {
                return Task.FromResult(func());
            }
            catch (Exception caught)
            {
                return Task.FromException<T>(caught);
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                completion.TrySetResult(func());
            }
            catch (Exception caught)
            {
                Fail(completion, caught);
            }
        });
        return completion.Task;
    }

    /// <summary>Runs asynchronous <paramref name="work"/> on this dispatcher; its awaits come back here.</summary>
    public Task InvokeAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(async () =>
        {
            await work();
            return true;
        });
    }

    /// <summary>Runs asynchronous <paramref name="work"/> on this dispatcher and returns its result.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (CheckAccess())
        {
            return work();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => _ = RunAndComplete(work, completion));
        return completion.Task;
    }

    /// <summary>Reports the exception of a posted item that nobody awaited.</summary>
    protected void OnUnhandledException(Exception error)
    {
        var handler = UnhandledException;
        if (handler is null)
        {
            // Nobody listens: keep it visible rather than swallowing it.
            System.Diagnostics.Trace.TraceError($"Unhandled exception on the presentation dispatcher: {error}");
        }
        else
        {
            handler(error);
        }
    }

    private static async Task RunAndComplete<T>(Func<Task<T>> work, TaskCompletionSource<T> completion)
    {
        try
        {
            completion.TrySetResult(await work());
        }
        catch (Exception caught)
        {
            Fail(completion, caught);
        }
    }

    private static void Fail<T>(TaskCompletionSource<T> completion, Exception caught)
    {
        if (caught is OperationCanceledException canceled)
        {
            completion.TrySetCanceled(canceled.CancellationToken);
        }
        else
        {
            completion.TrySetException(caught);
        }
    }

    private sealed class HostDispatcher(Action<Action> post, Func<bool> checkAccess) : PresentationDispatcher
    {
        public override bool CheckAccess() => checkAccess();

        public override void Post(SendOrPostCallback callback, object? state) =>
            post(() =>
            {
                try
                {
                    callback(state);
                }
                catch (Exception caught)
                {
                    OnUnhandledException(caught);
                }
            });
    }
}
