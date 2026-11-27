using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The repositories a host hands to a session: every operation through the entity's data policy
/// (<see cref="ShoppingPolicies"/>) and then the business rules (<see cref="ShoppingRules"/>), for whoever
/// <paramref name="actors"/> says is acting. The repositories given are the trusted inside: the rules read them, and so
/// do the login and the demo data.
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
        var policies = new ShoppingPolicies(store);
        var ruled = ShoppingRules.Wrap(store, actors, time ?? TimeProvider.System);
        var protection = new Protection(actors, store.Transactions);
        return new ShoppingRepositories(
            new ProtectedUserRepository(ruled.Users, policies.Users, protection),
            new ProtectedProductRepository(ruled.Products, policies.Products, protection),
            new ProtectedPurchaseRepository(ruled.Purchases, policies.Purchases, protection),
            new ProtectedPurchaseItemRepository(ruled.PurchaseItems, policies.Items, protection),
            new ProtectedPurchaseAmendmentRepository(ruled.Amendments, policies.Amendments, protection),
            new ProtectedPurchaseAmendmentLineRepository(ruled.AmendmentLines, policies.AmendmentLines, protection),
            store.Transactions);
    }
}
