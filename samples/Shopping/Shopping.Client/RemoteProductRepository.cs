using Shopping.Domain.Products;
using Wdc.Rest.Client;

namespace Shopping.Client;

public sealed class RemoteProductRepository(RestClient client)
    : RemoteRepository<Product, ProductCriteria, long>(client, "/api/repo/product", Product.Model, () => new ProductCriteria()),
        IProductRepository
{
    public async Task<byte[]?> FetchImage(long productId, CancellationToken cancellationToken = default)
    {
        // 404: no such product (KeyNotFoundException); 204: a product with no image.
        var body = await Client.Get($"{BasePath}/{productId}/image", cancellationToken);
        return body.Length == 0 ? null : body;
    }
}
