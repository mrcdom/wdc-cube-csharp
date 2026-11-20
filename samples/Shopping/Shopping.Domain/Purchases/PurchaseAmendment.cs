using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Purchases;

/// <summary>
/// An errata of a purchase already made: who corrected it, when, why, and what each item went from
/// and to. Inserting it is what applies the correction to the purchase's items; once inserted it does not change.
/// </summary>
public sealed class PurchaseAmendment : IKeyedEntity
{
    public static readonly EntityModel<PurchaseAmendment> Model = EntityModel.For(() => new PurchaseAmendment())
        .Key(ValueKinds.Int64, "id", a => a.Id, (a, v) => a.Id = v)
        .Field(ValueKinds.Instant, "date", a => a.Date, (a, v) => a.Date = v)
        .Field(ValueKinds.String, "reason", a => a.Reason, (a, v) => a.Reason = v)
        .Reference("purchase", a => a.Purchase, (a, v) => a.Purchase = v, () => Purchase.Model)
        .Reference("author", a => a.Author, (a, v) => a.Author = v, () => User.Model)
        .Collection("lines", a => a.Lines, (a, v) => a.Lines = v, () => PurchaseAmendmentLine.Model,
            () => new PurchaseAmendmentLineCriteria())
        .Build();

    public long? Id { get; set; }

    /// <summary>When the correction was made (set by whoever applies it, not by the request).</summary>
    public DateTimeOffset? Date { get; set; }

    /// <summary>Why: required.</summary>
    public string? Reason { get; set; }

    public Purchase? Purchase { get; set; }

    /// <summary>Who corrected it (set by whoever applies it: the actor).</summary>
    public User? Author { get; set; }

    public List<PurchaseAmendmentLine>? Lines { get; set; }

    object? IKeyedEntity.Key => Id;
}
