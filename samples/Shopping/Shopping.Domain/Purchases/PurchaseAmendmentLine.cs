using Shopping.Domain.Products;
using Wdc.Domain;

namespace Shopping.Domain.Purchases;

/// <summary>
/// One item corrected by an errata: the amount it had and the one it has now (0: removed). The item is kept by its
/// key only, since a removed item no longer exists; the product and the price say what it was.
/// </summary>
public sealed class PurchaseAmendmentLine : IKeyedEntity
{
    public static readonly EntityModel<PurchaseAmendmentLine> Model = EntityModel.For(() => new PurchaseAmendmentLine())
        .Key(ValueKinds.Int64, "id", l => l.Id, (l, v) => l.Id = v)
        .Field(ValueKinds.Int64, "itemId", l => l.ItemId, (l, v) => l.ItemId = v)
        .Field(ValueKinds.Decimal, "price", l => l.Price, (l, v) => l.Price = v)
        .Field(ValueKinds.Int32, "amountBefore", l => l.AmountBefore, (l, v) => l.AmountBefore = v)
        .Field(ValueKinds.Int32, "amountAfter", l => l.AmountAfter, (l, v) => l.AmountAfter = v)
        .Reference("amendment", l => l.Amendment, (l, v) => l.Amendment = v, () => PurchaseAmendment.Model)
        .Reference("product", l => l.Product, (l, v) => l.Product = v, () => Product.Model)
        .Build();

    public long? Id { get; set; }

    /// <summary>The item corrected (of the amendment's purchase).</summary>
    public long? ItemId { get; set; }

    /// <summary>The unit price of the item, as bought (filled in from the item).</summary>
    public decimal? Price { get; set; }

    /// <summary>The amount before the correction (filled in from the item; if sent, it must match it).</summary>
    public int? AmountBefore { get; set; }

    /// <summary>The amount after it: smaller than before; 0 removes the item.</summary>
    public int? AmountAfter { get; set; }

    public PurchaseAmendment? Amendment { get; set; }

    /// <summary>The item's product (filled in from the item).</summary>
    public Product? Product { get; set; }

    object? IKeyedEntity.Key => Id;
}
