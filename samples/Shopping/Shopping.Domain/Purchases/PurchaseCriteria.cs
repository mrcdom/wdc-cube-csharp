using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public sealed class PurchaseCriteria : Criteria<PurchaseCriteria, Purchase, PurchaseOrderBy>
{
    public PurchaseCriteria()
    {
        PurchaseId = Comparable(ValueKinds.Int64, "purchaseId");
        UserId = Equality(ValueKinds.Int64, "userId");
        BuyDate = Comparable(ValueKinds.Instant, "buyDate");
        ProductId = Equality(ValueKinds.Int64, "productId");
        CustomerName = Text("customerName");
    }

    public ComparableCriterion<PurchaseCriteria, long?> PurchaseId { get; }

    public Criterion<PurchaseCriteria, long?> UserId { get; }

    public ComparableCriterion<PurchaseCriteria, DateTimeOffset?> BuyDate { get; }

    /// <summary>Purchases that contain the product (a field of the items, not of the purchase).</summary>
    public Criterion<PurchaseCriteria, long?> ProductId { get; }

    /// <summary>Purchases of users whose name matches (a field of the user, not of the purchase).</summary>
    public TextCriterion<PurchaseCriteria> CustomerName { get; }
}
