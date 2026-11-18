using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>The four repositories over one database.</summary>
public sealed class Shopping(Database database) : IShoppingSide
{
    public Database Database { get; } = database;

    public SqlRepository<Product, ProductCriteria, long> Products { get; } = new(database, ShoppingMappings.Product);

    public SqlRepository<User, UserCriteria, long> Users { get; } = new(database, ShoppingMappings.User);

    public SqlRepository<Purchase, PurchaseCriteria, long> Purchases { get; } = new(database, ShoppingMappings.Purchase);

    public SqlRepository<PurchaseItem, PurchaseItemCriteria, long> Items { get; } = new(database, ShoppingMappings.PurchaseItem);

    ITransactionService IShoppingSide.Tx => Database;

    bool IShoppingSide.InTransaction => Database.InTransaction;

    IRepository<Product, ProductCriteria, long> IShoppingSide.Products => Products;

    IRepository<User, UserCriteria, long> IShoppingSide.Users => Users;

    IRepository<Purchase, PurchaseCriteria, long> IShoppingSide.Purchases => Purchases;

    IRepository<PurchaseItem, PurchaseItemCriteria, long> IShoppingSide.Items => Items;
}
