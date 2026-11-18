using Microsoft.Extensions.Time.Testing;
using Shopping.Persistence;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Main;
using Wdc.Cube;
using Wdc.Cube.Testing;

namespace Shopping.Presentation.Tests;

/// <summary>A Shopping database in a throwaway schema, with the demo's own DDL and data (decision D-14).</summary>
public class ShoppingDatabaseFixture : IAsyncLifetime
{
    public ShoppingDatabaseFixture()
        : this(DatabaseProvider.PostgreSql, "WDC_TEST_POSTGRES")
    {
    }

    protected ShoppingDatabaseFixture(DatabaseProvider provider, string variable)
    {
        Provider = provider;
        Variable = variable;
    }

    public DatabaseProvider Provider { get; }

    public string Variable { get; }

    public ShoppingStore? Store { get; private set; }

    public string SkipReason => $"{Variable} is not set (run: source scripts/test-env.sh)";

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { } connectionString)
        {
            return;
        }

        Store = ShoppingStore.Open(Provider, connectionString, "wdc_test_" + Guid.NewGuid().ToString("N")[..10]);
        await Store.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        if (Store is not null)
        {
            await Store.Drop();
            await Store.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

public sealed class ShoppingSqlServerFixture() : ShoppingDatabaseFixture(DatabaseProvider.SqlServer, "WDC_TEST_SQLSERVER");

/// <summary>Starts the Shopping application at an address, with no view.</summary>
internal sealed class Shop
{
    private Shop(ShoppingStore store, string address, ISessionStore session)
    {
        History = new TestHistoryManager(address);
        Session = session;
        App = new MainPresenter(History, store.Repositories, session, timeProvider: Time);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));

    public TestHistoryManager History { get; }

    public ISessionStore Session { get; }

    public MainPresenter App { get; }

    public static async Task<Shop> Start(ShoppingDatabaseFixture fixture, string address = "", ISessionStore? session = null)
    {
        Assert.SkipWhen(fixture.Store is null, fixture.SkipReason);
        var shop = new Shop(fixture.Store!, address, session ?? new MemorySessionStore());
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
        var login = PresenterOf<Modules.Login.LoginPresenter>(Places.Login);
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
        var home = Maybe<Modules.Home.HomePresenter>(Places.Home);
        return home is not null && (home.Scope.Products?.Loading == true || home.Scope.Purchases?.Loading == true)
               || Maybe<Modules.Login.LoginPresenter>(Places.Login)?.Scope.Loading == true
               || Maybe<Modules.Home.Cart.CartPresenter>(Places.Cart)?.Scope.Buying == true;
    }

    public async Task Do(Func<Task> action)
    {
        await action();
        await Settle();
    }
}
