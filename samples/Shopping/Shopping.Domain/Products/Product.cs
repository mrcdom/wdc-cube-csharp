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

public enum ProductOrderBy
{
    OldestFirst,
    NewestFirst,
    NameAToZ,
    CheapestFirst,
    MostExpensiveFirst,
}

public sealed class ProductCriteria : Criteria<ProductCriteria, Product, ProductOrderBy>
{
    public ProductCriteria()
    {
        ProductId = Comparable(ValueKinds.Int64, "productId");
        Name = Text("name");
        Price = Comparable(ValueKinds.Decimal, "price");
        Description = Text("description");
    }

    public ComparableCriterion<ProductCriteria, long?> ProductId { get; }

    public TextCriterion<ProductCriteria> Name { get; }

    public ComparableCriterion<ProductCriteria, decimal?> Price { get; }

    public TextCriterion<ProductCriteria> Description { get; }
}

public interface IProductRepository : IRepository<Product, ProductCriteria, long>
{
    /// <summary>The image of a product: <see langword="null"/> when it has none.</summary>
    /// <exception cref="KeyNotFoundException">No such product.</exception>
    Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default);
}
