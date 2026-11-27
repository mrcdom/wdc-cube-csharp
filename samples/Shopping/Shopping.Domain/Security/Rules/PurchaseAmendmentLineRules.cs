using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The lines of an errata enter only with it, which fills them in, and then do not change nor leave, but by maintenance.</summary>
internal sealed class PurchaseAmendmentLineRules(IPurchaseAmendmentLineRepository inner, IActorSource actors)
    : RuledRepository<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria, long>(inner, actors), IPurchaseAmendmentLineRepository
{
    public override Task<bool> Insert(PurchaseAmendmentLine bean, PurchaseAmendmentLine? projection = null,
        CancellationToken cancellationToken = default) =>
        Maintains ? base.Insert(bean, projection, cancellationToken) : throw new AccessDeniedException("A line enters only with its errata.");

    public override Task<bool> Update(PurchaseAmendmentLine newBean, PurchaseAmendmentLine? oldBean = null,
        PurchaseAmendmentLine? projection = null, CancellationToken cancellationToken = default) =>
        Maintains ? base.Update(newBean, oldBean, projection, cancellationToken) : throw new AccessDeniedException("An errata does not change.");

    public override Task<int> Delete(PurchaseAmendmentLineCriteria criteria, CancellationToken cancellationToken = default) =>
        Maintains ? base.Delete(criteria, cancellationToken) : throw new AccessDeniedException("An errata is not deleted.");
}
