using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public sealed class PurchaseItemCriteria : Criteria<PurchaseItemCriteria, PurchaseItem, PurchaseItemOrderBy>
{
    public PurchaseItemCriteria()
    {
        PurchaseItemId = Comparable(ValueKinds.Int64, "purchaseItemId");
        PurchaseId = Equality(ValueKinds.Int64, "purchaseId");
        ProductId = Equality(ValueKinds.Int64, "productId");
        UserId = Equality(ValueKinds.Int64, "userId");
        Amount = Comparable(ValueKinds.Int32, "amount");
        Price = Comparable(ValueKinds.Decimal, "price");
    }

    /// <summary>Items of the purchases of this user (virtual: through the purchase).</summary>
    public Criterion<PurchaseItemCriteria, long?> UserId { get; }

    public ComparableCriterion<PurchaseItemCriteria, long?> PurchaseItemId { get; }

    public Criterion<PurchaseItemCriteria, long?> PurchaseId { get; }

    public Criterion<PurchaseItemCriteria, long?> ProductId { get; }

    public ComparableCriterion<PurchaseItemCriteria, int?> Amount { get; }

    public ComparableCriterion<PurchaseItemCriteria, decimal?> Price { get; }
}
