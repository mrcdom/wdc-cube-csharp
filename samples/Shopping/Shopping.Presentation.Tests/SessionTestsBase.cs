using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Login;
using static Shopping.Presentation.Tests.Shown;

namespace Shopping.Presentation.Tests;

/// <summary>Login, guard and session (F5).</summary>
public abstract class SessionTestsBase(ShoppingDatabaseFixture db)
{
    [Fact]
    public async Task WithoutASession_EveryRestrictedAddressLandsOnLogin()
    {
        foreach (var address in new[] { "", "home", "home/cart", "home/product?pr-id=1", "home/receipt?rc-id=1" })
        {
            var shop = await Shop.Start(db, address);

            Assert.NotNull(shop.Maybe<LoginPresenter>(LoginKeys.Place));
            Assert.Null(shop.Maybe<HomePresenter>(HomeKeys.Place));
            Assert.Equal("login", shop.History.Token);
        }
    }

    [Fact]
    public async Task WrongCredentials_StayOnLoginWithTheMessage()
    {
        var shop = await Shop.Start(db);

        await shop.Login("admin", "wrong");

        var login = shop.PresenterOf<LoginPresenter>(LoginKeys.Place);
        Assert.Equal(LoginPresenter.NotRecognized, login.Scope.ErrorMessage);
        Assert.False(login.Scope.Loading);
        Assert.Null(shop.App.Subject);
    }

    [Fact]
    public async Task RightCredentials_OpenTheSessionAndGoHome()
    {
        var shop = await Shop.Start(db);

        await shop.Login("admin", "admin");

        var home = shop.PresenterOf<HomePresenter>(HomeKeys.Place);
        Assert.Equal("Hello, John Smith", En(home.Scope.Greeting));
        Assert.Equal("home", shop.History.Token);
        Assert.NotNull(await shop.Session.Get(SubjectSession.Key));
    }

    [Fact]
    public async Task Reload_ComesBackLoggedIn_AtTheSameAddress()
    {
        var first = await Shop.LoggedIn(db, "fulano");

        var reloaded = await Shop.Start(db, "home", first.Session); // F5: same tab, same session storage

        Assert.Equal("Hello, John Doe", En(reloaded.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Greeting));
    }

    [Fact]
    public async Task Exit_ClosesTheSessionAndGoesToLogin()
    {
        var shop = await Shop.LoggedIn(db, "beotrano");

        await shop.Do(shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.OnExit);

        Assert.Null(shop.App.Subject);
        Assert.Null(await shop.Session.Get(SubjectSession.Key));
        Assert.Equal("login", shop.History.Token);
    }
}
