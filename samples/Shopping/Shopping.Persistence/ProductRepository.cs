using Shopping.Domain.Products;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>Products: their table, their criteria, and the image apart (it only comes when asked for).</summary>
public sealed class ProductRepository(Database database)
    : SqlRepository<Product, ProductCriteria, long>(database, Mapping), IProductRepository
{
    /// <summary>How a product is stored; every relation that reaches a product uses it (and its conditions).</summary>
    public static readonly EntityMapping<Product> Mapping = EntityMapping.For(Product.Model, "product", "pr")
        .Key("id", generated: true)
        .Column("name")
        .Column("price")
        .Column("description")
        .Column("image")
        .Conditions<ProductCriteria>(Conditions)
        .Ordering<ProductOrderBy>(o => o switch
        {
            ProductOrderBy.NewestFirst => [SortSpec.Desc("id")],
            ProductOrderBy.NameAToZ => [SortSpec.Asc("name")],
            ProductOrderBy.CheapestFirst => [SortSpec.Asc("price")],
            ProductOrderBy.MostExpensiveFirst => [SortSpec.Desc("price")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    public async Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        var product = await FetchById(productId, new Product { Image = Mark.Bytes }, cancellationToken)
                      ?? throw new KeyNotFoundException($"No product {productId}.");
        return product.Image;
    }

    /// <summary>The product's criteria on its table.</summary>
    private static ISqlFragment? Conditions(ProductCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.ProductId),
        t.Translate("name", criteria.Name),
        t.Translate("price", criteria.Price),
        t.Translate("description", criteria.Description),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);
}
