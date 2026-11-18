using Wdc.Cube.Tests.Navigation;

namespace Wdc.Cube.Tests.Dispatching;

/// <summary>How <see cref="CubeApplication"/> uses its dispatcher (decision D-01).</summary>
public sealed class ApplicationDispatchTests
{
    [Fact]
    public async Task PresenterCode_AlwaysRunsOnTheDispatcher_EvenWhenNavigationStartsOutside()
    {
        var app = new NavApp();

        await app.Flip(NavPlaces.ProductDetail); // called from the test thread
        await app.Flip(NavPlaces.Settings); // releases the previous presenters

        Assert.NotEmpty(app.AccessLog);
        Assert.All(app.AccessLog, Assert.True);
    }

    [Fact]
    public async Task SynchronousStateMembers_RefuseCallersOutsideTheDispatcher()
    {
        var app = new NavApp();
        await app.Flip(NavPlaces.Home);

        Assert.Throws<InvalidOperationException>(() => app.NewFlipIntent(NavPlaces.Home));
        Assert.Throws<InvalidOperationException>(() => app.NewIntentFromString("root"));
        Assert.Throws<InvalidOperationException>(app.UpdateHistory);
        Assert.Throws<InvalidOperationException>(app.Release);

        // The same members work when entered properly.
        var token = await app.InvokeAsync(() => Task.FromResult(app.NewFlipIntent(NavPlaces.Home).ToString()));
        Assert.Equal("root/home", token);
    }

    [Fact]
    public async Task Redirect_StillRetargetsTheRunningNavigation_WhenThePresenterLeftTheDispatcher()
    {
        // A presenter that (wrongly) used ConfigureAwait(false) calls Flip from a pool thread. Flip marshals itself
        // back; the dispatcher is free because the navigation is awaiting that presenter, so the redirect still
        // lands inside the same attempt.
        var app = new NavApp();
        await app.Flip(NavPlaces.Home);
        app.Behave(NavPlaces.Products, new() { RedirectTo = NavPlaces.Cart, EscapeDispatcherBeforeRedirect = true });

        var reachedTarget = await app.Flip(NavPlaces.ProductDetail);

        Assert.False(reachedTarget);
        Assert.Same(NavPlaces.Cart, app.LastPlace);
        Assert.True(app.CreatedAt(NavPlaces.Products).Single().Released);
        Assert.Null(app.GetPresenter(NavPlaces.Home));
    }

    [Fact]
    public async Task ErrorsNobodyAwaited_ReachUnexpected()
    {
        var app = new NavApp();
        var error = new InvalidOperationException("posted");

        app.Dispatcher.Post(() => throw error);
        await app.InvokeAsync(() => { }); // the queue is FIFO: once this ran, the failing item ran too

        Assert.Contains(error, app.UnexpectedErrors);
    }

    [Fact]
    public async Task Release_DisposesTheDispatcherTheApplicationCreated_ButNotOneItWasGiven()
    {
        var owned = new NavApp();
        await owned.InvokeAsync(owned.Release);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owned.Flip(NavPlaces.Root));
        Assert.Throws<ObjectDisposedException>(() => owned.Dispatcher.Post(() => { }));

        using var shared = new SerialDispatcher();
        var guest = new NavApp(shared);
        await guest.InvokeAsync(guest.Release);
        Assert.Equal(1, await shared.InvokeAsync(() => 1)); // still alive for its owner
    }
}
