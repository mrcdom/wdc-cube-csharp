using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The items of the purchases. An item takes the price of its product, and enters a purchase only with it: once the
/// purchase is made its items do not enter, change or leave, for every role of the application. Only the maintenance
/// role (<see cref="Permissions.Maintains"/>) may amend them.
/// </summary>
public static class PurchaseItemPolicy
{
    /// <param name="store">The repositories the rules read (the trusted inside, not the protected ones).</param>
    public static RepositoryPolicy<PurchaseItem, PurchaseItemCriteria, long> Create(ShoppingRepositories store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new RepositoryPolicy<PurchaseItem, PurchaseItemCriteria, long>(PurchaseItem.Model, () => new PurchaseItemCriteria())
        {
            Authorize = Permissions.Allow("purchase-item"),
            Scope = (c, criteria) => Permissions.Reach(c, id => criteria.UserId.Restrict(id)),
            RestrictToId = (criteria, id) => criteria.PurchaseItemId.Restrict(id),
            SanitizeProjection = (_, i) => UserPolicy.DropPassword(i.Purchase?.User),
            SanitizeOutput = (_, i) => UserPolicy.DropPassword(i.Purchase?.User),
            BeforeInsert = async (c, item, ct) =>
            {
                var purchase = await RequireOwnPurchase(store, c, item.Purchase?.Id, ct);
                // In cascade, the item enters with its purchase (a purchase update by these roles is refused before).
                if (!c.InCascade && !Permissions.Maintains(c.Actor)
                    && await PurchasePolicy.AnyMade(store, new PurchaseCriteria().PurchaseId.Eq(purchase), ct))
                {
                    throw new AccessDeniedException("The purchase was already made: no item enters it.");
                }

                item.Price = await PriceOf(store, item.Product?.Id, ct);
            },
            BeforeUpdate = async (c, item, written, ct) =>
            {
                if (written.Purchase is not null)
                {
                    await RequireOwnPurchase(store, c, item.Purchase?.Id, ct);
                }

                if (Permissions.Maintains(c.Actor))
                {
                    return;
                }

                if (await AnyOfAPurchaseMade(store, new PurchaseItemCriteria().PurchaseItemId.Eq(item.Id), ct))
                {
                    throw new AccessDeniedException("The purchase was already made: its items do not change.");
                }

                if (written.Price is not null)
                {
                    throw new AccessDeniedException("The price of an item comes from the product.");
                }
            },
            BeforeDelete = async (c, criteria, ct) =>
            {
                if (!Permissions.Maintains(c.Actor) && await AnyOfAPurchaseMade(store, criteria, ct))
                {
                    throw new AccessDeniedException("The purchase was already made: its items cannot be removed.");
                }
            },
        };
    }

    /// <summary>The purchase the item goes into, if the actor reaches it.</summary>
    private static async Task<long> RequireOwnPurchase(ShoppingRepositories store, PolicyContext context, long? purchaseId,
        CancellationToken cancellationToken)
    {
        if (purchaseId is not { } id)
        {
            throw new InvalidRequestException("An item needs its purchase.");
        }

        var criteria = new PurchaseCriteria().PurchaseId.Restrict(id);
        Permissions.Reach(context, owner => criteria.UserId.Restrict(owner));
        if (await store.Purchases.Count(criteria, cancellationToken) == 0)
        {
            throw new AccessDeniedException("Not your purchase.");
        }

        return id;
    }

    private static async Task<bool> AnyOfAPurchaseMade(ShoppingRepositories store, PurchaseItemCriteria criteria,
        CancellationToken cancellationToken)
    {
        var projection = criteria.Projection;
        try
        {
            criteria.Projection = new PurchaseItem { Purchase = new Purchase { BuyDate = Mark.Instant } };
            var items = await store.PurchaseItems.Fetch(criteria, cancellationToken: cancellationToken);
            return items.Any(i => i.Purchase?.BuyDate is not null);
        }
        finally
        {
            criteria.Projection = projection;
        }
    }

    private static async Task<decimal?> PriceOf(ShoppingRepositories store, long? productId, CancellationToken cancellationToken)
    {
        var product = productId is { } id
            ? await store.Products.FetchById(id, new Product { Price = Mark.Decimal }, cancellationToken)
            : null;
        return product?.Price ?? throw new InvalidRequestException($"No product {productId}.");
    }
}
