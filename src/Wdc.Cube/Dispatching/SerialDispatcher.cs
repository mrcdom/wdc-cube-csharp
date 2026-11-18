using System.Threading.Channels;

namespace Wdc.Cube;

/// <summary>
/// A dispatcher with its own queue, drained by a single consumer on the thread pool: the default for an application
/// that has no UI thread to borrow (servers, tests, background hosts).
/// </summary>
/// <remarks>
/// Each item runs with this dispatcher's <see cref="SynchronizationContext"/> installed, so the continuation of an
/// <c>await</c> is queued here again. Items never overlap; they interleave only at <c>await</c> points, exactly like an
/// event loop. Consecutive items may run on different pool threads.
/// </remarks>
public sealed class SerialDispatcher : PresentationDispatcher, IDisposable
{
    [ThreadStatic]
    private static SerialDispatcher? t_running;

    private readonly Channel<WorkItem> _queue =
        Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Context _context;

    /// <summary>Creates the dispatcher and starts draining its queue.</summary>
    public SerialDispatcher()
    {
        _context = new Context(this);
        Completion = Task.Run(Drain);
    }

    /// <summary>Completes when the dispatcher has been disposed and its queue drained.</summary>
    public Task Completion { get; }

    /// <inheritdoc/>
    public override bool CheckAccess() => ReferenceEquals(t_running, this);

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The dispatcher was disposed.</exception>
    public override void Post(SendOrPostCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!_queue.Writer.TryWrite(new WorkItem(callback, state)))
        {
            throw new ObjectDisposedException(nameof(SerialDispatcher));
        }
    }

    /// <summary>Stops accepting items; what is already queued still runs.</summary>
    public void Dispose() => _queue.Writer.TryComplete();

    private async Task Drain()
    {
        var reader = _queue.Reader;

        // Infrastructure, not presentation: the loop itself must not capture any context.
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                Execute(item);
            }
        }
    }

    private void Execute(WorkItem item)
    {
        var previous = SynchronizationContext.Current;
        t_running = this;
        SynchronizationContext.SetSynchronizationContext(_context);
        try
        {
            item.Callback(item.State);
        }
        catch (Exception caught)
        {
            OnUnhandledException(caught);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            t_running = null;
        }
    }

    private readonly record struct WorkItem(SendOrPostCallback Callback, object? State);

    private sealed class Context(SerialDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => dispatcher.Post(d, state);

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (dispatcher.CheckAccess())
            {
                d(state);
            }
            else
            {
                dispatcher.InvokeAsync(() => d(state)).GetAwaiter().GetResult();
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
