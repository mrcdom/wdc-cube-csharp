using Shopping.Domain.Products;
using Wdc.Rest.Client;

namespace Shopping.Client;

public sealed class RemoteProductRepository(RestClient client)
    : RemoteRepository<Product, ProductCriteria, long>(client, "/api/repo/product", Product.Model, () => new ProductCriteria()),
        IProductRepository
{
    public async Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        var body = await Client.GetOrNull($"{BasePath}/{productId}/image", cancellationToken)
                   ?? throw new KeyNotFoundException($"No product {productId}.");
        return body.Length == 0 ? null : body; // 204: no image
    }
}
