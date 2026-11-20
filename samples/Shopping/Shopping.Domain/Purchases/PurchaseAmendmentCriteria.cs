using Wdc.Domain;

namespace Shopping.Domain.Purchases;

public sealed class PurchaseAmendmentCriteria : Criteria<PurchaseAmendmentCriteria, PurchaseAmendment, PurchaseAmendmentOrderBy>
{
    public PurchaseAmendmentCriteria()
    {
        PurchaseAmendmentId = Comparable(ValueKinds.Int64, "purchaseAmendmentId");
        PurchaseId = Equality(ValueKinds.Int64, "purchaseId");
        AuthorId = Equality(ValueKinds.Int64, "authorId");
        UserId = Equality(ValueKinds.Int64, "userId");
        Date = Comparable(ValueKinds.Instant, "date");
    }

    public ComparableCriterion<PurchaseAmendmentCriteria, long?> PurchaseAmendmentId { get; }

    public Criterion<PurchaseAmendmentCriteria, long?> PurchaseId { get; }

    public Criterion<PurchaseAmendmentCriteria, long?> AuthorId { get; }

    /// <summary>Errata of the purchases of this user (virtual: through the purchase).</summary>
    public Criterion<PurchaseAmendmentCriteria, long?> UserId { get; }

    public ComparableCriterion<PurchaseAmendmentCriteria, DateTimeOffset?> Date { get; }
}
