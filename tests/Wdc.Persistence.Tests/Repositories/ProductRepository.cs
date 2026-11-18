using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>Products of the test domain: the mapping and the criteria translation, as a repository owns them.</summary>
public sealed class ProductRepository(Database database) : SqlRepository<Product, ProductCriteria, long>(database, Mapping)
{
    public static readonly EntityMapping<Product> Mapping = EntityMapping.For(Product.Model, "product", "p")
        .Key("id", generated: true)
        .Column("name")
        .Column("price")
        .Column("description")
        .Column("image")
        .Collection("items", "product_id", () => PurchaseItemRepository.Mapping) // where it was sold
        .Conditions<ProductCriteria>(Conditions)
        .Ordering<ProductOrderBy>(o => o switch
        {
            ProductOrderBy.NameAToZ => [SortSpec.Asc("name")],
            ProductOrderBy.CheapestFirst => [SortSpec.Asc("price")],
            _ => [],
        })
        .Build();

    private static ISqlFragment? Conditions(ProductCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.ProductId),
        t.Translate("name", criteria.Name),
        t.Translate("price", criteria.Price),
    ]);
}
