using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The repositories a host hands to a session (decision D-24): every operation through the entity's policy, for
/// whoever <paramref name="actors"/> says is acting. The repositories given are the trusted inside — the rules read
/// them, and so do the login and the demo data.
/// </summary>
public static class ShoppingProtection
{
    public static ShoppingRepositories Protect(ShoppingRepositories store, IActorSource actors)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(actors);
        var protection = new Protection(actors, store.Transactions);
        return new ShoppingRepositories(
            new ProtectedUserRepository(store.Users, protection),
            new ProtectedProductRepository(store.Products, protection),
            new ProtectedPurchaseRepository(store.Purchases, store, protection),
            new ProtectedPurchaseItemRepository(store.PurchaseItems, store, protection),
            store.Transactions);
    }
}
