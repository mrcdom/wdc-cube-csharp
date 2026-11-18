using Microsoft.AspNetCore.Components;
using Wdc.Cube.Testing;

namespace Wdc.Cube.Blazor.Tests;

/// <summary>
/// The browser address in Blazor Server, where NavigateTo is confirmed later by the browser. Found in a real browser:
/// two updates in a row wrote two history entries, and Back stayed on the same page.
/// </summary>
public sealed class NavigationHistoryManagerTests
{
    private readonly DelayedNavigationManager _navigation = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly NavigationHistoryManager _history;
    private readonly CubeApplication _app;

    public NavigationHistoryManagerTests()
    {
        _history = new NavigationHistoryManager(_navigation);
        _app = new CubeApplication(_history, HistoryPlaces.All, dispatcher: _dispatcher);
    }

    private void On(Func<Task> work) => _dispatcher.Run(work);

    [Fact]
    public void TwoUpdatesBeforeTheBrowserConfirms_WriteOneEntry()
    {
        On(() => _app.Flip(HistoryPlaces.Cart));
        On(() =>
        {
            _app.UpdateHistory(); // e.g. the end of the action that flipped
            return Task.CompletedTask;
        });

        Assert.Equal(["cart"], _navigation.Requested);
    }

    [Fact]
    public void TheConfirmationOfOurOwnWrite_IsNotTheUserMoving()
    {
        On(() => _app.Flip(HistoryPlaces.Product));
        On(() => _app.Flip(HistoryPlaces.Cart));
        _navigation.ConfirmAll(); // "product" arrives while the application is already on "cart"
        _dispatcher.RunPending();

        Assert.Same(HistoryPlaces.Cart, _app.LastPlace); // not flipped back to product
        Assert.Equal(["product", "cart"], _navigation.Requested);
    }

    [Fact]
    public void Back_FlipsToTheAddressTheBrowserShows()
    {
        On(() => _app.Flip(HistoryPlaces.Product));
        On(() => _app.Flip(HistoryPlaces.Cart));
        _navigation.ConfirmAll();
        _dispatcher.RunPending();

        _navigation.UserGoesTo("product"); // Back
        _dispatcher.Run(() => Task.Delay(10));
        _dispatcher.RunPending();

        Assert.Same(HistoryPlaces.Product, _app.LastPlace);
        Assert.Equal(["product", "cart"], _navigation.Requested); // and nothing new was written
    }
}

internal static class HistoryPlaces
{
    public static readonly Place Product = Place.Create("product", _ => new Leaf());
    public static readonly Place Cart = Place.Create("cart", _ => new Leaf());
    public static readonly IReadOnlyList<Place> All = [Product, Cart];

    private sealed class Leaf : ICubePresenter
    {
        public Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last) => Task.FromResult(true);

        public void PublishParameters(FlipIntent intent)
        {
        }

        public void Release()
        {
        }
    }
}

/// <summary>Behaves like Blazor Server's: the URI changes only when the "browser" confirms.</summary>
internal sealed class DelayedNavigationManager : NavigationManager
{
    private readonly Queue<string> _unconfirmed = new();

    public DelayedNavigationManager() => Initialize("http://localhost/", "http://localhost/");

    public List<string> Requested { get; } = [];

    public void ConfirmAll()
    {
        while (_unconfirmed.TryDequeue(out var uri))
        {
            Uri = uri;
            NotifyLocationChanged(isInterceptedLink: false);
        }
    }

    public void UserGoesTo(string relative)
    {
        Uri = BaseUri + relative;
        NotifyLocationChanged(isInterceptedLink: false);
    }

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Requested.Add(ToBaseRelativePath(uri));
        _unconfirmed.Enqueue(uri);
    }
}
