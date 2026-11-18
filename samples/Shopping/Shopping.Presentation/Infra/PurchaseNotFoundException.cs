namespace Shopping.Presentation.Infra;

/// <summary>The address names a purchase that does not exist or belongs to someone else: the navigation is refused.</summary>
public sealed class PurchaseNotFoundException(long purchaseId) : Exception($"No purchase {purchaseId} for this user.")
{
    public long PurchaseId { get; } = purchaseId;
}
