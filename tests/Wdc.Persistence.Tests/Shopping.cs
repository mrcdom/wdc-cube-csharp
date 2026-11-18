using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>The four repositories over one database.</summary>
public sealed class Shopping(Database database) : IShoppingSide
{
    public Database Database { get; } = database;

    public ProductRepository Products { get; } = new(database);

    public UserRepository Users { get; } = new(database);

    public PurchaseRepository Purchases { get; } = new(database);

    public PurchaseItemRepository Items { get; } = new(database);

    ITransactionService IShoppingSide.Tx => Database;

    bool IShoppingSide.InTransaction => Database.InTransaction;

    IRepository<Product, ProductCriteria, long> IShoppingSide.Products => Products;

    IRepository<User, UserCriteria, long> IShoppingSide.Users => Users;

    IRepository<Purchase, PurchaseCriteria, long> IShoppingSide.Purchases => Purchases;

    IRepository<PurchaseItem, PurchaseItemCriteria, long> IShoppingSide.Items => Items;
}
