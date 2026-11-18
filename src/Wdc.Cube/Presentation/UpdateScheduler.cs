namespace Wdc.Cube;

/// <summary>
/// Batches the scope updates of one application into windows (about 16 ms by default), so a burst of changes reaches
/// each view once. The presentation layer is responsible for not abusing the rendering technology, whatever it is.
/// </summary>
/// <remarks>
/// The first change after a flush arms a single timer; when it fires, the flush runs on the application's
/// dispatcher. A guarded action flushes at its end without waiting for the window (<see cref="FlushNow"/>).
/// </remarks>
public sealed class UpdateScheduler : IDisposable
{
    /// <summary>The default window: one frame at 60 Hz.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(16);

    /// <summary>How many rounds one flush may take when rendering keeps changing scopes, before it is called a loop.</summary>
    internal const int MaxRounds = 20;

    private readonly TimeProvider _time;
    private readonly Action<string, Exception> _report;
    private readonly List<ScopeUpdateManager> _dirty = [];
    private ITimer? _timer;
    private bool _flushing;
    private bool _disposed;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="dispatcher">Where flushes run.</param>
    /// <param name="time">The clock; <see cref="TimeProvider.System"/> when omitted (tests pass a fake one).</param>
    /// <param name="window">How long to gather changes; <see cref="DefaultWindow"/> when omitted.</param>
    /// <param name="report">Receives errors raised while flushing.</param>
    public UpdateScheduler(PresentationDispatcher dispatcher, TimeProvider? time = null, TimeSpan? window = null,
        Action<string, Exception>? report = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        Dispatcher = dispatcher;
        _time = time ?? TimeProvider.System;
        Window = window ?? DefaultWindow;
        _report = report ?? ((message, error) => System.Diagnostics.Trace.TraceError($"{message}: {error}"));
    }

    /// <summary>Where flushes run.</summary>
    public PresentationDispatcher Dispatcher { get; }

    /// <summary>How long changes are gathered before views are told.</summary>
    public TimeSpan Window { get; }

    /// <summary>Whether some scope is dirty and waiting for a flush.</summary>
    public bool HasPendingUpdates => _dirty.Count > 0;

    /// <summary>
    /// Flushes now, on the caller's turn of the dispatcher: what changed during the flush (derived state, views that
    /// change scopes while drawing) is flushed too, up to a limit that reports an update loop.
    /// </summary>
    public void FlushNow()
    {
        Dispatcher.VerifyAccess();
        if (_flushing)
        {
            return; // a view flushing from inside a flush: the running flush will get to it
        }

        CancelTimer();
        _flushing = true;
        try
        {
            for (var round = 1; _dirty.Count > 0; round++)
            {
                if (round > MaxRounds)
                {
                    Report("Scope update loop",
                        new InvalidOperationException(
                            $"Scopes kept changing for {MaxRounds} rounds of one flush; the rest goes to the next window."));
                    break;
                }

                var batch = _dirty.ToArray();
                _dirty.Clear();
                foreach (var manager in batch)
                {
                    manager.Flush();
                }
            }
        }
        finally
        {
            _flushing = false;
            if (_dirty.Count > 0)
            {
                ArmTimer();
            }
        }
    }

    /// <summary>Stops the timer; later changes are not flushed by time anymore.</summary>
    public void Dispose()
    {
        _disposed = true;
        CancelTimer();
    }

    internal void Schedule(ScopeUpdateManager manager)
    {
        if (!_dirty.Contains(manager))
        {
            _dirty.Add(manager);
        }

        if (!_flushing)
        {
            ArmTimer();
        }
    }

    internal void Forget(ScopeUpdateManager manager) => _dirty.Remove(manager);

    internal void Report(string message, Exception error) => _report(message, error);

    private void ArmTimer()
    {
        if (_timer is not null || _disposed)
        {
            return;
        }

        _timer = _time.CreateTimer(static state => ((UpdateScheduler)state!).OnTimer(), this, Window,
            Timeout.InfiniteTimeSpan);
    }

    // On the timer's thread: hop to the dispatcher.
    private void OnTimer()
    {
        try
        {
            Dispatcher.Post(() =>
            {
                CancelTimer();
                if (!_disposed)
                {
                    FlushNow();
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // The application is gone; nothing to draw.
        }
    }

    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
