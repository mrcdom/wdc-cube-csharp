using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The purchase item repository as an actor sees it: through <see cref="PurchaseItemPolicy"/>.</summary>
public sealed class ProtectedPurchaseItemRepository(IPurchaseItemRepository inner, DataPolicy<PurchaseItem, PurchaseItemCriteria> policy,
    Protection protection)
    : ProtectedRepository<PurchaseItem, PurchaseItemCriteria, long>(inner, policy, (c, id) => c.PurchaseItemId.Restrict(id), protection),
        IPurchaseItemRepository;
