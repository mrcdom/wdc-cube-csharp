namespace Wdc.Domain.Tests;

public sealed class PurchaseItemCriteria : Criteria<PurchaseItemCriteria, PurchaseItem, PurchaseItemOrderBy>
{
    public PurchaseItemCriteria()
    {
        PurchaseId = Equality(ValueKinds.Int64, "purchaseId");
        Amount = Comparable(ValueKinds.Int32, "amount");
    }

    public Criterion<PurchaseItemCriteria, long?> PurchaseId { get; }

    public ComparableCriterion<PurchaseItemCriteria, int?> Amount { get; }
}
