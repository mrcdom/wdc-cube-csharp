using Microsoft.Extensions.Time.Testing;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Admin;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Login;
using Wdc.Cube;
using Wdc.Cube.Testing;

namespace Shopping.Presentation.Tests;

/// <summary>Starts the Shopping application at an address, with no view.</summary>
internal sealed class Shop
{
    private Shop(ShoppingDatabaseFixture fixture, string address, ISessionStore session)
    {
        History = new TestHistoryManager(address);
        Session = session;
        var (repositories, authentication) = fixture.Connect();
        App = new RootPresenter(History, repositories, session, timeProvider: Time, authentication: authentication);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));

    public TestHistoryManager History { get; }

    public ISessionStore Session { get; }

    public RootPresenter App { get; }

    public static async Task<Shop> Start(ShoppingDatabaseFixture fixture, string address = "", ISessionStore? session = null)
    {
        Assert.SkipWhen(fixture.Store is null, fixture.SkipReason);
        var shop = new Shop(fixture, address, session ?? new MemorySessionStore());
        await shop.App.Start();
        await shop.Settle();
        return shop;
    }

    /// <summary>Starts and logs in.</summary>
    public static async Task<Shop> LoggedIn(ShoppingDatabaseFixture fixture, string user, string address = "")
    {
        var shop = await Start(fixture, address);
        if (shop.App.Subject is null)
        {
            await shop.Login(user, user);
        }

        return shop;
    }

    public T PresenterOf<T>(Place place)
        where T : class, ICubePresenter => App.PresenterOf<T>(place);

    public T? Maybe<T>(Place place)
        where T : class, ICubePresenter => App.GetPresenter<T>(place);

    public async Task Login(string user, string password)
    {
        var login = PresenterOf<Modules.Login.LoginPresenter>(LoginKeys.Place);
        await login.Scope.OnEnter(user, password);
        await Settle();
    }

    /// <summary>
    /// Lets the loads that run in parallel (the panels, by design) finish and flushes the scopes. Waits on what the
    /// screens say ("loading"), not on a fixed delay: a remote database answers slower than a local one.
    /// </summary>
    public async Task Settle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            await App.Settle();
            await Task.Delay(20, TestContext.Current.CancellationToken);
            await App.Settle();
        }
        while (await App.InvokeAsync(() => Task.FromResult(IsBusy())) && DateTime.UtcNow < deadline);

        Assert.False(IsBusy(), $"Still loading after 15 s. Error toast: {App.Scope.ErrorMessage ?? "(none)"}");
    }

    private bool IsBusy()
    {
        var home = Maybe<Modules.Home.HomePresenter>(HomeKeys.Place);
        return home is not null && (home.Scope.Products?.Loading == true || home.Scope.Purchases?.Loading == true)
               || Maybe<Modules.Login.LoginPresenter>(LoginKeys.Place)?.Scope.Loading == true
               || Maybe<Modules.Home.Cart.CartPresenter>(CartKeys.Place)?.Scope.Buying == true
               || Maybe<Modules.Home.Admin.AdminPresenter>(AdminKeys.Place)?.Scope.Loading == true
               || Maybe<Modules.Home.Admin.Amend.AmendPresenter>(AmendKeys.Place)?.Scope.Saving == true;
    }

    public async Task Do(Func<Task> action)
    {
        await action();
        await Settle();
    }
}
