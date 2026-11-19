using Shopping.Domain.Products;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The products: the catalogue every role reads; who writes it is in <see cref="Permissions"/>.</summary>
public static class ProductPolicy
{
    public static RepositoryPolicy<Product, ProductCriteria, long> Create() => new(Product.Model, () => new ProductCriteria())
    {
        Authorize = Permissions.Allow("product"),
    };
}
