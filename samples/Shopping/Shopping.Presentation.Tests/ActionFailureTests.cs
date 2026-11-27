using Shopping.Domain;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Login;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>
/// A service that does not answer, during an action: the screen that knows says so where the user is, and the
/// application still hears of it.
/// </summary>
public sealed class ActionFailureTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    [Fact]
    public async Task Login_ServerNotAnswering_IsSaidOnTheLoginCard()
    {
        var shop = await Shop.Start(db, connect: c => (c.Repositories, new Unreachable()));

        await shop.Login("fulano", "fulano");

        Assert.Equal(LoginPresenter.CommunicationFailure, shop.PresenterOf<LoginPresenter>(LoginKeys.Place).Scope.ErrorMessage);
        Assert.Equal(RootPresenter.GenericError, shop.App.Scope.ErrorMessage); // the application heard of it too
        Assert.Null(shop.App.Subject);
    }

    [Fact]
    public async Task Buying_DatabaseNotAnswering_IsSaidOnTheCart_AndTheCartIsKept()
    {
        var shop = await Shop.Start(db, connect: c => (c.Repositories with { Transactions = new Unreachable() }, c.Authentication));
        await shop.Login("fulano", "fulano");
        var products = shop.App.GetPresenter<Modules.Home.HomePresenter>(HomeKeys.Place)!.Scope.Products!;
        await shop.Do(() => products.OnOpenProduct(products.Products[0].Id));
        await shop.Do(() => shop.PresenterOf<ProductPresenter>(ProductKeys.Place).Scope.OnAddToCart(1));
        var cart = shop.PresenterOf<CartPresenter>(CartKeys.Place);

        await shop.Do(() => cart.Scope.OnBuy());

        Assert.Equal(CartMessages.Unavailable, cart.Scope.ErrorMessage);
        Assert.False(cart.Scope.Buying);
        Assert.Single(cart.Scope.Lines);
        Assert.Equal("home/cart", shop.History.Token);
    }

    /// <summary>A login and a transaction service whose server does not answer.</summary>
    private sealed class Unreachable : IAuthentication, ITransactionService
    {
        public Task<Subject?> Login(string userName, string password, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("connection refused");

        public Task<Subject?> Resume(Subject subject, CancellationToken cancellationToken = default) => Task.FromResult<Subject?>(null);

        public Task Logout(Subject subject, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<T> Execute<T>(Propagation propagation, Func<ITransactionContext, Task<T>> work, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("connection refused");

        public Task AfterCommit(Func<Task> task) => task();
    }
}
