using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The purchase item repository as an actor sees it: through <see cref="PurchaseItemPolicy"/>.</summary>
public sealed class ProtectedPurchaseItemRepository(IPurchaseItemRepository inner, ShoppingRepositories store, Protection protection)
    : ProtectedRepository<PurchaseItem, PurchaseItemCriteria, long>(inner, PurchaseItemPolicy.Create(store), protection), IPurchaseItemRepository;
