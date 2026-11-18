using Wdc.Domain;

namespace Shopping.Domain.Products;

public interface IProductRepository : IRepository<Product, ProductCriteria, long>
{
    /// <summary>The image of a product: <see langword="null"/> when it has none.</summary>
    /// <exception cref="KeyNotFoundException">No such product.</exception>
    Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default);
}
