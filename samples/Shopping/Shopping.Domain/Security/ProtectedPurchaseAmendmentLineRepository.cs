using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The errata line repository as an actor sees it: through <see cref="PurchaseAmendmentLinePolicy"/>.</summary>
public sealed class ProtectedPurchaseAmendmentLineRepository(IPurchaseAmendmentLineRepository inner,
    DataPolicy<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria> policy, Protection protection)
    : ProtectedRepository<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria, long>(inner, policy,
        (c, id) => c.PurchaseAmendmentLineId.Restrict(id), protection), IPurchaseAmendmentLineRepository;
