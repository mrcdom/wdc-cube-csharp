using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The errata repository as an actor sees it: through <see cref="PurchaseAmendmentPolicy"/>.</summary>
public sealed class ProtectedPurchaseAmendmentRepository(IPurchaseAmendmentRepository inner,
    DataPolicy<PurchaseAmendment, PurchaseAmendmentCriteria> policy, Protection protection)
    : ProtectedRepository<PurchaseAmendment, PurchaseAmendmentCriteria, long>(inner, policy,
        (c, id) => c.PurchaseAmendmentId.Restrict(id), protection), IPurchaseAmendmentRepository;
