namespace Wdc.Cube.Tests.Dispatching;

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
