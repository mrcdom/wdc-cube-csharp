using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The purchases. A purchase belongs to whoever bought it; once made (it has its date) it does not change, for every
/// role of the application — not its fields, not its items, and it is not deleted. Only the maintenance role
/// (<see cref="Permissions.Maintains"/>) may amend or delete it.
/// </summary>
public static class PurchasePolicy
{
    /// <param name="store">The repositories the rules read (the trusted inside, not the protected ones).</param>
    public static RepositoryPolicy<Purchase, PurchaseCriteria, long> Create(ShoppingRepositories store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new RepositoryPolicy<Purchase, PurchaseCriteria, long>(Purchase.Model, () => new PurchaseCriteria())
        {
            Authorize = Permissions.Allow("purchase"),
            Scope = (c, criteria) => Permissions.Reach(c, id => criteria.UserId.Restrict(id)),
            RestrictToId = (criteria, id) => criteria.PurchaseId.Restrict(id),
            SanitizeProjection = (_, p) => UserPolicy.DropPassword(p.User),
            SanitizeOutput = (_, p) => UserPolicy.DropPassword(p.User),
            BeforeInsert = (c, purchase, _) =>
            {
                if (Permissions.ReachOf(c.Actor) is { } owner)
                {
                    purchase.User = new User { Id = owner }; // bought by whoever is acting, whatever was sent
                }

                // The items, inserted in cascade, go through the purchase-item policy (price from the product).
                return ValueTask.CompletedTask;
            },
            BeforeUpdate = async (c, purchase, written, ct) =>
            {
                if (Permissions.Maintains(c.Actor))
                {
                    return;
                }

                if (await AnyMade(store, new PurchaseCriteria().PurchaseId.Eq(purchase.Id), ct))
                {
                    throw new AccessDeniedException("The purchase was already made: it does not change.");
                }

                if (written.User is not null && Permissions.ReachOf(c.Actor) is not null)
                {
                    throw new AccessDeniedException("A purchase cannot change hands.");
                }
            },
            BeforeDelete = async (c, criteria, ct) =>
            {
                if (!Permissions.Maintains(c.Actor) && await AnyMade(store, criteria, ct))
                {
                    throw new AccessDeniedException("The purchase was already made: it is not deleted.");
                }
            },
        };
    }

    /// <summary>Whether any of the purchases that match was already made.</summary>
    internal static async Task<bool> AnyMade(ShoppingRepositories store, PurchaseCriteria criteria, CancellationToken cancellationToken)
    {
        var projection = criteria.Projection;
        try
        {
            criteria.Projection = new Purchase { BuyDate = Mark.Instant };
            var purchases = await store.Purchases.Fetch(criteria, cancellationToken: cancellationToken);
            return purchases.Any(p => p.BuyDate is not null);
        }
        finally
        {
            criteria.Projection = projection;
        }
    }
}
