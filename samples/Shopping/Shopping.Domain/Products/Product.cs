using Wdc.Domain;

namespace Shopping.Domain.Products;

public sealed class Product : IKeyedEntity
{
    public static readonly EntityModel<Product> Model = EntityModel.For(() => new Product())
        .Key(ValueKinds.Int64, "id", p => p.Id, (p, v) => p.Id = v)
        .Field(ValueKinds.String, "name", p => p.Name, (p, v) => p.Name = v)
        .Field(ValueKinds.Decimal, "price", p => p.Price, (p, v) => p.Price = v)
        .Field(ValueKinds.String, "description", p => p.Description, (p, v) => p.Description = v)
        .Field(ValueKinds.Bytes, "image", p => p.Image, (p, v) => p.Image = v, inDefaultProjection: false)
        .Build();

    public long? Id { get; set; }

    public string? Name { get; set; }

    public decimal? Price { get; set; }

    /// <summary>HTML: a <c>&lt;ul&gt;&lt;li&gt;</c> list, or plain text.</summary>
    public string? Description { get; set; }

    /// <summary>PNG bytes. Only comes when asked for.</summary>
    public byte[]? Image { get; set; }

    object? IKeyedEntity.Key => Id;
}
