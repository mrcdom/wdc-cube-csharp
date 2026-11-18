using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wdc.Domain;
using Wdc.Domain.Tests;
using Wdc.Rest.Client;
using Wdc.Rest.Server;

namespace Wdc.Persistence.Tests;

/// <summary>An in-memory server with the repository routes over the local repositories (no socket).</summary>
public sealed class RestTestHost : IAsyncDisposable
{
    private RestTestHost(WebApplication app) => App = app;

    public WebApplication App { get; }

    public RemoteTransactionCoordinator Coordinator => App.Services.GetRequiredService<RemoteTransactionCoordinator>();

    public static async Task<RestTestHost> Start(Shopping local, Action<RestServerOptions>? configure = null,
        Action<RemoteTransactionOptions>? configureTransactions = null, Action<WebApplication>? mapMore = null,
        Action<RepositoryRpc<Purchase, PurchaseCriteria, long>>? configurePurchases = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(local);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (time is not null)
        {
            builder.Services.AddSingleton(time);
        }

        builder.Services.AddSingleton<ITransactionService>(local.Database);
        builder.Services.AddSingleton<IDetachedTransactionSource>(local.Database);
        builder.Services.AddCubeRest(configure, configureTransactions: o =>
        {
            o.ReapInterval = TimeSpan.FromHours(1); // the tests call Reap themselves
            configureTransactions?.Invoke(o);
        });
        var app = builder.Build();

        new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model, () => new ProductCriteria(),
            _ => local.Products).Map(app);
        new RepositoryRpc<User, UserCriteria, long>("/api/repo/user", User.Model, () => new UserCriteria(),
            _ => local.Users).Map(app);
        var purchases = new RepositoryRpc<Purchase, PurchaseCriteria, long>("/api/repo/purchase", Purchase.Model,
            () => new PurchaseCriteria(), _ => local.Purchases);
        configurePurchases?.Invoke(purchases);
        purchases.Map(app);
        new RepositoryRpc<PurchaseItem, PurchaseItemCriteria, long>("/api/repo/purchase-item", PurchaseItem.Model,
            () => new PurchaseItemCriteria(), _ => local.Items).Map(app);
        app.MapRemoteTransactions();
        mapMore?.Invoke(app);

        await app.StartAsync();
        return new RestTestHost(app);
    }

    /// <summary>A client of this server (a new client id each time, unless options say otherwise).</summary>
    public RestClient NewClient(RestClientOptions? options = null) => new(App.GetTestClient(), options);

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

/// <summary>The four repositories and the transactions, through HTTP.</summary>
public sealed class RemoteShopping(RestClient client) : IShoppingSide
{
    private readonly RemoteTransactionService _tx = new(client);

    public RestClient Client { get; } = client;

    public ITransactionService Tx => _tx;

    public bool InTransaction => _tx.InTransaction;

    public IRepository<Product, ProductCriteria, long> Products { get; } =
        new RemoteRepository<Product, ProductCriteria, long>(client, "/api/repo/product", Product.Model, () => new ProductCriteria());

    public IRepository<User, UserCriteria, long> Users { get; } =
        new RemoteRepository<User, UserCriteria, long>(client, "/api/repo/user", User.Model, () => new UserCriteria());

    public IRepository<Purchase, PurchaseCriteria, long> Purchases { get; } =
        new RemoteRepository<Purchase, PurchaseCriteria, long>(client, "/api/repo/purchase", Purchase.Model, () => new PurchaseCriteria());

    public IRepository<PurchaseItem, PurchaseItemCriteria, long> Items { get; } =
        new RemoteRepository<PurchaseItem, PurchaseItemCriteria, long>(client, "/api/repo/purchase-item", PurchaseItem.Model,
            () => new PurchaseItemCriteria());
}

public sealed class PostgreSqlHttpFixture : PostgreSqlFixture
{
    private RestTestHost? _host;

    public override bool IsRemote => true;

    protected override async Task OnReady()
    {
        _host = await RestTestHost.Start(Shopping!);
        Side = new RemoteShopping(_host.NewClient());
    }

    protected override async Task OnDisposing()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }
}

public sealed class SqlServerHttpFixture : SqlServerFixture
{
    private RestTestHost? _host;

    public override bool IsRemote => true;

    protected override async Task OnReady()
    {
        _host = await RestTestHost.Start(Shopping!);
        Side = new RemoteShopping(_host.NewClient());
    }

    protected override async Task OnDisposing()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }
}
