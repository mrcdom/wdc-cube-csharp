using Wdc.Cube.Testing;

namespace Wdc.Cube.Tests.Dispatching;

public sealed class ManualDispatcherTests
{
    private readonly ManualDispatcher _dispatcher = new();

    [Fact]
    public void NothingRuns_UntilTheTestPumps()
    {
        var log = new List<int>();
        _dispatcher.Post(() => log.Add(1));
        _dispatcher.Post(() => log.Add(2));

        Assert.Empty(log);
        Assert.Equal(2, _dispatcher.PendingCount);

        Assert.True(_dispatcher.RunOne());
        Assert.Equal([1], log);
        Assert.Equal(1, _dispatcher.RunPending());
        Assert.Equal([1, 2], log);
    }

    [Fact]
    public void Run_PumpsUntilTheWorkCompletes_IncludingRealAsyncContinuations()
    {
        var result = _dispatcher.Run(async () =>
        {
            Assert.True(_dispatcher.CheckAccess());
            await Task.Delay(10); // completes on a timer thread; the continuation is posted back here
            Assert.True(_dispatcher.CheckAccess());
            return 7;
        });

        Assert.Equal(7, result);
    }

    [Fact]
    public void Run_RethrowsTheWorksException()
    {
        var error = new InvalidOperationException("x");
        var caught = Assert.Throws<InvalidOperationException>(() => _dispatcher.Run(async () =>
        {
            await Task.Yield();
            throw error;
        }));

        Assert.Same(error, caught);
    }

    [Fact]
    public void Run_TimesOut_InsteadOfHangingTheSuite()
    {
        var never = new TaskCompletionSource();
        Assert.Throws<TimeoutException>(() => _dispatcher.Run(() => never.Task, TimeSpan.FromMilliseconds(100)));
    }
}
