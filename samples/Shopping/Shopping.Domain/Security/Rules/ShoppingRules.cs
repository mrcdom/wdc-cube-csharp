using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The business rules of the Shopping data, for every role: what is not about who reaches what. A price comes from the
/// product; a purchase made does not change, nor do its items, and is not deleted; an errata is the correction of a
/// purchase, recorded by itself, with the server's date and author; its lines enter only with it. Only the maintenance
/// role (<see cref="Permissions.Maintains"/>) has the rules marked as relaxable relaxed.
/// </summary>
/// <remarks>The repositories it returns sit between the protected ones (the access, <see cref="ShoppingPolicies"/>)
/// and the store; the rules read and write the store, the trusted inside.</remarks>
public static class ShoppingRules
{
    /// <param name="store">The trusted inside: the repositories of the store.</param>
    /// <param name="actors">Who is acting, at each call (maintenance relaxes some rules; an errata records its author).</param>
    /// <param name="time">The clock of what the server dates (an errata).</param>
    public static ShoppingRepositories Wrap(ShoppingRepositories store, IActorSource actors, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(time);
        return new ShoppingRepositories(
            new UserRules(store.Users, actors),
            store.Products,
            new PurchaseRules(store.Purchases, store, actors),
            new PurchaseItemRules(store.PurchaseItems, store, actors),
            new PurchaseAmendmentRules(store.Amendments, store, time, actors),
            new PurchaseAmendmentLineRules(store.AmendmentLines, actors),
            store.Transactions);
    }
}
