namespace Wdc.Cube.Testing;

/// <summary>
/// A dispatcher pumped by the test itself, on the test's thread: nothing runs until the test asks, and things run in
/// exactly the order they were queued. Use it when a test must control interleavings (an action arriving in the
/// middle of a navigation, a timer firing between two awaits).
/// </summary>
public sealed class ManualDispatcher : PresentationDispatcher
{
    [ThreadStatic]
    private static ManualDispatcher? t_running;

    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly object _lock = new(); // not System.Threading.Lock: Monitor.Wait/PulseAll need a monitor
    private readonly Context _context;

    /// <summary>Creates the dispatcher with an empty queue.</summary>
    public ManualDispatcher()
    {
        _context = new Context(this);
        UnhandledException += error => Unhandled.Add(error);
    }

    /// <summary>Exceptions of posted items that nobody awaited, in order.</summary>
    public List<Exception> Unhandled { get; } = [];

    /// <summary>How many items are waiting.</summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    /// <inheritdoc/>
    public override bool CheckAccess() => ReferenceEquals(t_running, this);

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_lock)
        {
            _queue.Enqueue((callback, state));
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>Runs one queued item. Returns whether there was one.</summary>
    public bool RunOne()
    {
        (SendOrPostCallback Callback, object? State) item;
        lock (_lock)
        {
            if (!_queue.TryDequeue(out item))
            {
                return false;
            }
        }

        var previous = SynchronizationContext.Current;
        var previousRunning = t_running;
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
            t_running = previousRunning;
        }

        return true;
    }

    /// <summary>Runs queued items, including the ones they queue, until the queue is empty. Returns how many ran.</summary>
    public int RunPending()
    {
        var count = 0;
        while (RunOne())
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Runs <paramref name="work"/> on this dispatcher and pumps on the calling thread until it completes, waiting
    /// for continuations of real asynchronous operations (I/O, delays) as they arrive.
    /// </summary>
    /// <exception cref="TimeoutException">The work did not complete within <paramref name="timeout"/>.</exception>
    public void Run(Func<Task> work, TimeSpan? timeout = null) =>
        Run(async () =>
        {
            await work();
            return true;
        }, timeout);

    /// <summary>Runs <paramref name="work"/> on this dispatcher, pumping until it completes, and returns its result.</summary>
    /// <exception cref="TimeoutException">The work did not complete within <paramref name="timeout"/>.</exception>
    public T Run<T>(Func<Task<T>> work, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        var task = InvokeAsync(work);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (!task.IsCompleted)
        {
            if (RunOne())
            {
                continue;
            }

            lock (_lock)
            {
                if (_queue.Count == 0 && !task.IsCompleted)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw new TimeoutException("The work did not complete on the manual dispatcher in time.");
                    }

                    // Woken by Post; the short cap re-checks completions that do not post here.
                    Monitor.Wait(_lock, TimeSpan.FromMilliseconds(Math.Min(50, remaining.TotalMilliseconds)));
                }
            }
        }

        RunPending();
        return task.GetAwaiter().GetResult();
    }

    private sealed class Context(ManualDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => dispatcher.Post(d, state);

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (!dispatcher.CheckAccess())
            {
                throw new InvalidOperationException("Send from outside the manual dispatcher would deadlock the test.");
            }

            d(state);
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
