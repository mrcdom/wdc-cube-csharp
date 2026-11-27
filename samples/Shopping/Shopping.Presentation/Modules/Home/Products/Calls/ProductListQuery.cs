using Shopping.Domain;
using Shopping.Domain.Products;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Products.Calls;

/// <summary>The catalogue's cards, A to Z: id, name and price.</summary>
public sealed class ProductListQuery(ShoppingRepositories repositories, int limit) : ResourceQuery<IReadOnlyList<Product>>
{
    public override Task<IReadOnlyList<Product>> Run(CancellationToken cancellationToken = default) =>
        repositories.Products.Fetch(
            new ProductCriteria()
                .WithOrderBy(ProductOrderBy.NameAToZ)
                .WithProjection(new Product { Id = Mark.Int64, Name = Mark.String, Price = Mark.Decimal }),
            limit: limit,
            cancellationToken: cancellationToken);
}
