using Wdc.Domain;
using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

public sealed class PurchaseCriteria : Criteria<PurchaseCriteria, Purchase, PurchaseOrderBy>
{
    public PurchaseCriteria()
    {
        PurchaseId = Comparable(ValueKinds.Int64, "purchaseId");
        UserId = Equality(ValueKinds.Int64, "userId");
        ProductId = Equality(ValueKinds.Int64, "productId"); // virtual: purchases that contain the product
        BuyDate = Comparable(ValueKinds.Instant, "buyDate");
    }

    public ComparableCriterion<PurchaseCriteria, long?> PurchaseId { get; }

    public Criterion<PurchaseCriteria, long?> UserId { get; }

    public Criterion<PurchaseCriteria, long?> ProductId { get; }

    public ComparableCriterion<PurchaseCriteria, DateTimeOffset?> BuyDate { get; }
}
