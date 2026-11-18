using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>What the repository contract talks to: four repositories and the transactions, on one side or the other.</summary>
public interface IShoppingSide
{
    ITransactionService Tx { get; }

    bool InTransaction { get; }

    IRepository<Product, ProductCriteria, long> Products { get; }

    IRepository<User, UserCriteria, long> Users { get; }

    IRepository<Purchase, PurchaseCriteria, long> Purchases { get; }

    IRepository<PurchaseItem, PurchaseItemCriteria, long> Items { get; }
}
