namespace Wdc.Domain.Tests;

public sealed class PurchaseItem : IKeyedEntity
{
    public static readonly EntityModel<PurchaseItem> Model = EntityModel.For(() => new PurchaseItem())
        .Key(ValueKinds.Int64, "id", i => i.Id, (i, v) => i.Id = v)
        .Field(ValueKinds.Int32, "amount", i => i.Amount, (i, v) => i.Amount = v)
        .Field(ValueKinds.Decimal, "price", i => i.Price, (i, v) => i.Price = v)
        .Reference("purchase", i => i.Purchase, (i, v) => i.Purchase = v, () => Purchase.Model)
        .Reference("product", i => i.Product, (i, v) => i.Product = v, () => Product.Model)
        .Build();

    public long? Id { get; set; }

    public int? Amount { get; set; }

    public decimal? Price { get; set; }

    public Purchase? Purchase { get; set; }

    public Product? Product { get; set; }

    object? IKeyedEntity.Key => Id;
}
