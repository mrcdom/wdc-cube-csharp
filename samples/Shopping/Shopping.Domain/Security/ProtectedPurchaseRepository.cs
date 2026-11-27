using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The purchase repository as an actor sees it: through <see cref="PurchasePolicy"/>.</summary>
public sealed class ProtectedPurchaseRepository(IPurchaseRepository inner, DataPolicy<Purchase, PurchaseCriteria> policy, Protection protection)
    : ProtectedRepository<Purchase, PurchaseCriteria, long>(inner, policy, (c, id) => c.PurchaseId.Restrict(id), protection), IPurchaseRepository;
