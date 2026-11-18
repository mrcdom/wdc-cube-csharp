using Shopping.Domain.Products;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

public sealed class ProductRepository(Database database)
    : SqlRepository<Product, ProductCriteria, long>(database, ShoppingMappings.Product), IProductRepository
{
    public async Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        var product = await FetchById(productId, new Product { Image = Mark.Bytes }, cancellationToken)
                      ?? throw new KeyNotFoundException($"No product {productId}.");
        return product.Image;
    }
}
