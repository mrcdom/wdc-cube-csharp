using Wdc.Domain;
using Wdc.Domain.Tests;
using Wdc.Rest.Client;

namespace Wdc.Persistence.Tests;

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
