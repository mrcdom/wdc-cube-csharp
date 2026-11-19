namespace Wdc.Domain.Tests;

public sealed class PurchaseItemCriteria : Criteria<PurchaseItemCriteria, PurchaseItem, PurchaseItemOrderBy>
{
    public PurchaseItemCriteria()
    {
        PurchaseItemId = Comparable(ValueKinds.Int64, "purchaseItemId");
        PurchaseId = Equality(ValueKinds.Int64, "purchaseId");
        Amount = Comparable(ValueKinds.Int32, "amount");
    }

    public ComparableCriterion<PurchaseItemCriteria, long?> PurchaseItemId { get; }

    public Criterion<PurchaseItemCriteria, long?> PurchaseId { get; }

    public ComparableCriterion<PurchaseItemCriteria, int?> Amount { get; }
}
