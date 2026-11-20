using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public sealed class PurchaseAmendmentLineCriteria
    : Criteria<PurchaseAmendmentLineCriteria, PurchaseAmendmentLine, PurchaseAmendmentLineOrderBy>
{
    public PurchaseAmendmentLineCriteria()
    {
        PurchaseAmendmentLineId = Comparable(ValueKinds.Int64, "purchaseAmendmentLineId");
        PurchaseAmendmentId = Equality(ValueKinds.Int64, "purchaseAmendmentId");
        ItemId = Equality(ValueKinds.Int64, "itemId");
        UserId = Equality(ValueKinds.Int64, "userId");
    }

    public ComparableCriterion<PurchaseAmendmentLineCriteria, long?> PurchaseAmendmentLineId { get; }

    public Criterion<PurchaseAmendmentLineCriteria, long?> PurchaseAmendmentId { get; }

    public Criterion<PurchaseAmendmentLineCriteria, long?> ItemId { get; }

    /// <summary>Lines of errata of the purchases of this user (virtual: through the errata and its purchase).</summary>
    public Criterion<PurchaseAmendmentLineCriteria, long?> UserId { get; }
}
