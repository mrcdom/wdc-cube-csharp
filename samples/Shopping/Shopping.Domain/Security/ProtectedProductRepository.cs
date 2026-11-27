using Shopping.Domain.Products;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The products as an actor sees them; the image is read as the product's field it is.</summary>
public sealed class ProtectedProductRepository(IProductRepository inner, DataPolicy<Product, ProductCriteria> policy, Protection protection)
    : ProtectedRepository<Product, ProductCriteria, long>(inner, policy, (c, id) => c.ProductId.Restrict(id), protection), IProductRepository
{
    /// <exception cref="KeyNotFoundException">No such product within reach.</exception>
    public async Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        var product = await FetchById(productId, new Product { Image = Mark.Bytes }, cancellationToken)
                      ?? throw new KeyNotFoundException($"No product {productId}.");
        return product.Image;
    }
}
