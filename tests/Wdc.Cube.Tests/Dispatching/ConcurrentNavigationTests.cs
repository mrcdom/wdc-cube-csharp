using Wdc.Cube.Testing;
using Wdc.Cube.Tests.Navigation;

namespace Wdc.Cube.Tests.Dispatching;

/// <summary>
/// Navigation requests that arrive while another navigation is running (decision D-13). Every test drives a
/// <see cref="ManualDispatcher"/>, so the interleaving is exactly the one written.
/// </summary>
public sealed class ConcurrentNavigationTests
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly NavApp _app;

    public ConcurrentNavigationTests()
    {
        _app = new NavApp(_dispatcher);
        _dispatcher.Run(() => _app.Flip(NavPlaces.Root));
    }

    /// <summary>Starts a navigation to Products that waits on a gate inside its ApplyParameters.</summary>
    private (Task<bool> Slow, TaskCompletionSource Gate) StartSlowNavigationToProducts()
    {
        var gate = new TaskCompletionSource();
        _app.Behave(NavPlaces.Products, new() { Gate = gate });
        var slow = _app.Flip(NavPlaces.Products);
        _dispatcher.RunPending();
        Assert.True(_app.IsFlipping);
        return (slow, gate);
    }

    [Fact]
    public void RequestFromOutside_WaitsForTheRunningNavigation_ThenRuns()
    {
        var (slow, gate) = StartSlowNavigationToProducts();

        var click = _app.Flip(NavPlaces.Settings);
        _dispatcher.RunPending();
        Assert.False(click.IsCompleted); // waiting, not taking over
        Assert.Empty(_app.CreatedAt(NavPlaces.Settings));

        gate.SetResult();
        Assert.True(_dispatcher.Run(() => slow)); // the running one finishes where it was going
        Assert.True(_dispatcher.Run(() => click)); // then the waiting one runs

        Assert.Same(NavPlaces.Settings, _app.LastPlace);
        Assert.True(_app.CreatedAt(NavPlaces.Products).Single().Released);
        Assert.Equal(["root/products", "settings"], _app.History.Tokens.Skip(1));
    }

    [Fact]
    public void OnlyTheLatestRequestWaits_OlderWaitingOnesAreDiscarded()
    {
        var (slow, gate) = StartSlowNavigationToProducts();

        var first = _app.Flip(NavPlaces.Home);
        var second = _app.Flip(NavPlaces.Cart);
        var third = _app.Flip(NavPlaces.Settings);
        _dispatcher.RunPending();

        Assert.True(first.IsCompleted && second.IsCompleted); // discarded at once...
        Assert.False(_dispatcher.Run(() => first)); // ...with false
        Assert.False(_dispatcher.Run(() => second));
        Assert.False(third.IsCompleted);

        gate.SetResult();
        _dispatcher.Run(() => slow);
        Assert.True(_dispatcher.Run(() => third));

        Assert.Same(NavPlaces.Settings, _app.LastPlace);
        Assert.Empty(_app.CreatedAt(NavPlaces.Home));
        Assert.Empty(_app.CreatedAt(NavPlaces.Cart));
    }

    [Fact]
    public void WhenTheRunningNavigationFails_TheWaitingRequestIsDiscardedToo()
    {
        var gate = new TaskCompletionSource();
        var error = new InvalidOperationException("load failed");
        _app.Behave(NavPlaces.Products, new() { Gate = gate, Throw = error });
        var slow = _app.Flip(NavPlaces.Products);
        _dispatcher.RunPending();

        var click = _app.Flip(NavPlaces.Settings);
        _dispatcher.RunPending();

        gate.SetResult();
        var caught = Assert.Throws<InvalidOperationException>(() => _dispatcher.Run(() => slow));
        Assert.Same(error, caught);
        Assert.False(_dispatcher.Run(() => click));

        Assert.Same(NavPlaces.Root, _app.LastPlace); // the state before the failed attempt
        Assert.Empty(_app.CreatedAt(NavPlaces.Settings));
    }

    [Fact]
    public void RedirectFromInsideTheAttempt_StillTakesItOver()
    {
        // The guard pattern is untouched: a presenter that navigates from its ApplyParameters retargets the attempt.
        _app.Behave(NavPlaces.Products, new() { RedirectTo = NavPlaces.Cart });

        var reached = _dispatcher.Run(() => _app.Flip(NavPlaces.ProductDetail));

        Assert.False(reached);
        Assert.Same(NavPlaces.Cart, _app.LastPlace);
        Assert.Empty(_app.CreatedAt(NavPlaces.ProductDetail));
    }

    [Fact]
    public void WorkLeftRunningByAnEndedAttempt_CountsAsOutside()
    {
        // Home starts a load it does not await; the load navigates later, while another navigation runs. It carries
        // the ambient context of Home's attempt, which ended long ago: it must wait, not take over.
        var homeGate = new TaskCompletionSource();
        _app.Behave(NavPlaces.Home, new() { DeferredFlip = (homeGate, NavPlaces.Settings) });
        _dispatcher.Run(() => _app.Flip(NavPlaces.Home));

        var (slow, productsGate) = StartSlowNavigationToProducts();
        homeGate.SetResult();
        _dispatcher.RunPending();
        var deferred = _app.DeferredResults.Single();
        Assert.False(deferred.IsCompleted);

        productsGate.SetResult();
        Assert.True(_dispatcher.Run(() => slow)); // not retargeted by the stale work
        Assert.True(_dispatcher.Run(() => deferred));
        Assert.Same(NavPlaces.Settings, _app.LastPlace);
    }

    [Fact]
    public void HistoryChangeDuringANavigation_WaitsAndReadsTheLatestAddress()
    {
        var (slow, gate) = StartSlowNavigationToProducts();

        _app.History.Navigate("root/home");
        _app.History.Navigate("root/cart"); // the user pressed Back twice; the latest address wins
        _dispatcher.RunPending();

        gate.SetResult();
        _dispatcher.Run(() => slow);
        _dispatcher.RunPending();
        _dispatcher.Run(() => Task.Delay(10));

        Assert.Same(NavPlaces.Cart, _app.LastPlace);
        Assert.Empty(_app.CreatedAt(NavPlaces.Home));
    }
}
