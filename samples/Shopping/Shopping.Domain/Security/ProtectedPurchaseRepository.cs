using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The purchase repository as an actor sees it: through <see cref="PurchasePolicy"/>.</summary>
public sealed class ProtectedPurchaseRepository(IPurchaseRepository inner, ShoppingRepositories store, Protection protection)
    : ProtectedRepository<Purchase, PurchaseCriteria, long>(inner, PurchasePolicy.Create(store), protection), IPurchaseRepository;
