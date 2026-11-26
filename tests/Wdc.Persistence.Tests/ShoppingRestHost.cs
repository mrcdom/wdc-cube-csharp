using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Wdc.Domain;
using Wdc.Domain.Tests;
using Wdc.Rest.Client;
using Wdc.Rest.Server;
using Wdc.Rest.Testing;

namespace Wdc.Persistence.Tests;

/// <summary>The repository routes over the local repositories, on the in-memory test host.</summary>
public sealed class ShoppingRestHost : IAsyncDisposable
{
    private ShoppingRestHost(RestTestHost host) => Host = host;

    public RestTestHost Host { get; }

    public WebApplication App => Host.App;

    public RemoteTransactionCoordinator Coordinator => App.Services.GetRequiredService<RemoteTransactionCoordinator>();

    public static async Task<ShoppingRestHost> Start(Shopping local, Action<RestServerOptions>? configure = null,
        Action<RemoteTransactionOptions>? configureTransactions = null, Action<WebApplication>? mapMore = null,
        Func<HttpContext, IRepository<Purchase, PurchaseCriteria, long>>? purchases = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(local);
        var host = await RestTestHost.Start(builder =>
        {
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
        }, app =>
        {
            new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model, () => new ProductCriteria(),
                _ => local.Products).Map(app);
            new RepositoryRpc<User, UserCriteria, long>("/api/repo/user", User.Model, () => new UserCriteria(),
                _ => local.Users).Map(app);
            new RepositoryRpc<Purchase, PurchaseCriteria, long>("/api/repo/purchase", Purchase.Model,
                () => new PurchaseCriteria(), purchases ?? (_ => local.Purchases)).Map(app);
            new RepositoryRpc<PurchaseItem, PurchaseItemCriteria, long>("/api/repo/purchase-item", PurchaseItem.Model,
                () => new PurchaseItemCriteria(), _ => local.Items).Map(app);
            app.MapRemoteTransactions();
            mapMore?.Invoke(app);
        });
        return new ShoppingRestHost(host);
    }

    /// <summary>A client of this server (a new client id each time, unless options say otherwise).</summary>
    public RestClient NewClient(RestClientOptions? options = null) => Host.NewClient(options);

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
