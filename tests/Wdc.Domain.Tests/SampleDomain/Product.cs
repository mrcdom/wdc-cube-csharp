namespace Wdc.Domain.Tests;

// A small Shopping domain for the framework's tests.

public sealed class Product : IKeyedEntity
{
    public static readonly EntityModel<Product> Model = EntityModel.For(() => new Product())
        .Key(ValueKinds.Int64, "id", p => p.Id, (p, v) => p.Id = v)
        .Field(ValueKinds.String, "name", p => p.Name, (p, v) => p.Name = v)
        .Field(ValueKinds.Decimal, "price", p => p.Price, (p, v) => p.Price = v)
        .Field(ValueKinds.String, "description", p => p.Description, (p, v) => p.Description = v)
        .Field(ValueKinds.Bytes, "image", p => p.Image, (p, v) => p.Image = v, inDefaultProjection: false)
        .Collection("items", p => p.Items, (p, v) => p.Items = v, () => PurchaseItem.Model, () => new PurchaseItemCriteria())
        .Build();

    public long? Id { get; set; }

    public string? Name { get; set; }

    public decimal? Price { get; set; }

    public string? Description { get; set; }

    public byte[]? Image { get; set; }

    /// <summary>Where the product was sold: the purchase items that carry it (a multivalued field).</summary>
    public List<PurchaseItem>? Items { get; set; }

    object? IKeyedEntity.Key => Id;
}
