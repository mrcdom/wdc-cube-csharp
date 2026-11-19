using Shopping.Domain.Products;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The products as an actor sees them; the image is read like the rest of the product.</summary>
public sealed class ProtectedProductRepository(IProductRepository inner, Protection protection)
    : ProtectedRepository<Product, ProductCriteria, long>(inner, ProductPolicy.Create(), protection), IProductRepository
{
    public Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        Enter(RepositoryOperation.FetchById);
        return inner.FetchImage(productId, cancellationToken);
    }
}
