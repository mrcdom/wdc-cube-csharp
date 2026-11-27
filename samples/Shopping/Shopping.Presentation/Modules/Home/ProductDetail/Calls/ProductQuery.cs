using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Presentation.Infra;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.ProductDetail.Calls;

/// <summary>A product's page: name, price and description.</summary>
/// <exception cref="ProductNotFoundException">No such product.</exception>
public sealed class ProductQuery(ShoppingRepositories repositories, long productId) : ResourceQuery<Product>
{
    public override async Task<Product> Run(CancellationToken cancellationToken = default) =>
        await repositories.Products.FetchById(productId, new Product
        {
            Id = Mark.Int64,
            Name = Mark.String,
            Price = Mark.Decimal,
            Description = Mark.String,
        }, cancellationToken) ?? throw new ProductNotFoundException(productId);
}
