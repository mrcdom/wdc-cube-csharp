using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The errata repository as an actor sees it: through <see cref="PurchaseAmendmentPolicy"/>.</summary>
public sealed class ProtectedPurchaseAmendmentRepository(IPurchaseAmendmentRepository inner, ShoppingRepositories store,
    TimeProvider time, Protection protection)
    : ProtectedRepository<PurchaseAmendment, PurchaseAmendmentCriteria, long>(inner, PurchaseAmendmentPolicy.Create(store, time),
        protection), IPurchaseAmendmentRepository;
