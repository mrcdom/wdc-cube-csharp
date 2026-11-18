namespace Wdc.Cube.Tests.Dispatching;

public sealed class SerialDispatcherTests : IDisposable
{
    private readonly SerialDispatcher _dispatcher = new();

    public void Dispose() => _dispatcher.Dispose();

    [Fact]
    public async Task Items_NeverOverlap_EvenWhenPostedFromManyThreads()
    {
        var counter = 0; // deliberately not atomic: only serial execution keeps it exact
        var running = 0;
        var maxRunning = 0;

        var tasks = Enumerable.Range(0, 64).Select(_ => Task.Run(() => _dispatcher.InvokeAsync(() =>
        {
            var now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            for (var i = 0; i < 1000; i++)
            {
                counter++;
            }

            Thread.SpinWait(200);
            Interlocked.Decrement(ref running);
        })));
        await Task.WhenAll(tasks);

        Assert.Equal(64_000, counter);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Await_ComesBackToTheDispatcher()
    {
        var checks = await _dispatcher.InvokeAsync(async () =>
        {
            var before = _dispatcher.CheckAccess();
            await Task.Delay(5);
            var afterDelay = _dispatcher.CheckAccess();
            await Task.Yield();
            var afterYield = _dispatcher.CheckAccess();
            return (before, afterDelay, afterYield);
        });

        Assert.Equal((true, true, true), checks);
        Assert.False(_dispatcher.CheckAccess()); // the test itself is outside
    }

    [Fact]
    public async Task Items_InterleaveOnlyAtAwaitPoints()
    {
        var log = new List<string>();
        var gate = new TaskCompletionSource();

        var a = _dispatcher.InvokeAsync(async () =>
        {
            log.Add("A1");
            await gate.Task;
            log.Add("A2");
        });
        var b = _dispatcher.InvokeAsync(() =>
        {
            log.Add("B");
            gate.SetResult();
        });
        await Task.WhenAll(a, b);

        Assert.Equal(["A1", "B", "A2"], log);
    }

    [Fact]
    public async Task InvokeAsync_FromTheDispatcher_RunsInline()
    {
        var inline = await _dispatcher.InvokeAsync(() =>
        {
            var nested = _dispatcher.InvokeAsync(() => 42);
            return nested.IsCompletedSuccessfully && nested.Result == 42;
        });

        Assert.True(inline); // no deadlock, no reordering
    }

    [Fact]
    public async Task InvokeAsync_PropagatesTheExceptionToTheCaller()
    {
        var error = new InvalidOperationException("boom");

        var syncCaught = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _dispatcher.InvokeAsync(() => throw error));
        var asyncCaught = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _dispatcher.InvokeAsync(async () =>
            {
                await Task.Yield();
                throw error;
            }));

        Assert.Same(error, syncCaught);
        Assert.Same(error, asyncCaught);
    }

    [Fact]
    public async Task Post_ReportsWhatNobodyAwaited()
    {
        var reported = new TaskCompletionSource<Exception>();
        _dispatcher.UnhandledException += e => reported.TrySetResult(e);
        var error = new InvalidOperationException("lost");

        _dispatcher.Post(() => throw error);

        Assert.Same(error, await reported.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfigureAwaitFalse_LeavesTheDispatcher()
    {
        // The rule of D-01, demonstrated: after ConfigureAwait(false) the code is outside, and VerifyAccess says so.
        var (inside, outside, verifyFailed) = await _dispatcher.InvokeAsync(async () =>
        {
            var before = _dispatcher.CheckAccess();
            await Task.Delay(1).ConfigureAwait(false);
            var after = _dispatcher.CheckAccess();
            var failed = Record.Exception(_dispatcher.VerifyAccess) is InvalidOperationException;
            return (before, after, failed);
        });

        Assert.True(inside);
        Assert.False(outside);
        Assert.True(verifyFailed);
    }

    [Fact]
    public async Task Dispose_RunsWhatIsQueued_AndRefusesNewItems()
    {
        var ran = _dispatcher.InvokeAsync(() => 1);
        _dispatcher.Dispose();

        Assert.Equal(1, await ran);
        await _dispatcher.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => _dispatcher.Post(() => { }));
    }
}

public sealed class HostDispatcherTests : IDisposable
{
    // Stands in for a host's serial context (Blazor's renderer dispatcher, a UI thread).
    private readonly SerialDispatcher _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task FromHost_RunsOnTheHostAndAwaitsComeBackToIt()
    {
        var dispatcher = PresentationDispatcher.FromHost(action => _host.Post(action), _host.CheckAccess);

        var checks = await dispatcher.InvokeAsync(async () =>
        {
            var before = dispatcher.CheckAccess() && _host.CheckAccess();
            await Task.Delay(1);
            return before && dispatcher.CheckAccess();
        });

        Assert.True(checks);
    }

    [Fact]
    public async Task FromHost_ReportsPostedFailuresThroughTheAdapter()
    {
        var dispatcher = PresentationDispatcher.FromHost(action => _host.Post(action), _host.CheckAccess);
        var reported = new TaskCompletionSource<Exception>();
        dispatcher.UnhandledException += e => reported.TrySetResult(e);
        var error = new InvalidOperationException("host");

        dispatcher.Post(() => throw error);

        Assert.Same(error, await reported.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }
}
