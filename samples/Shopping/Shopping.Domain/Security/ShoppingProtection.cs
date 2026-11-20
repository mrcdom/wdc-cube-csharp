using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The repositories a host hands to a session (decision D-24): every operation through the entity's policy, for
/// whoever <paramref name="actors"/> says is acting. The repositories given are the trusted inside — the rules read
/// them, and so do the login and the demo data.
/// </summary>
public static class ShoppingProtection
{
    /// <param name="store">The trusted inside: the repositories of the store.</param>
    /// <param name="actors">Who is acting, at each call.</param>
    /// <param name="time">The clock of what the server dates (an errata); the system's by default.</param>
    public static ShoppingRepositories Protect(ShoppingRepositories store, IActorSource actors, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(actors);
        var protection = new Protection(actors, store.Transactions);
        return new ShoppingRepositories(
            new ProtectedUserRepository(store.Users, protection),
            new ProtectedProductRepository(store.Products, protection),
            new ProtectedPurchaseRepository(store.Purchases, store, protection),
            new ProtectedPurchaseItemRepository(store.PurchaseItems, store, protection),
            new ProtectedPurchaseAmendmentRepository(store.Amendments, store, time ?? TimeProvider.System, protection),
            new ProtectedPurchaseAmendmentLineRepository(store.AmendmentLines, protection),
            store.Transactions);
    }
}
