namespace Shopping.Presentation.Infra;

/// <summary>The address names a product that does not exist: the navigation is refused.</summary>
public sealed class ProductNotFoundException(long productId) : Exception($"No product {productId}.")
{
    public long ProductId { get; } = productId;
}
