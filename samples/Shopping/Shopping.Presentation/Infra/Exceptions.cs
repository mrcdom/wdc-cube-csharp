namespace Shopping.Presentation.Infra;

/// <summary>The address names a product that does not exist: the navigation is refused.</summary>
public sealed class ProductNotFoundException(long productId) : Exception($"No product {productId}.")
{
    public long ProductId { get; } = productId;
}

/// <summary>The address names a purchase that does not exist or belongs to someone else: the navigation is refused.</summary>
public sealed class PurchaseNotFoundException(long purchaseId) : Exception($"No purchase {purchaseId} for this user.")
{
    public long PurchaseId { get; } = purchaseId;
}
