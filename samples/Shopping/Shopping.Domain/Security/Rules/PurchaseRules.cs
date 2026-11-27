using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// A purchase is made when it is created; once made it does not change and is not deleted, but by maintenance. Its
/// items take the price of their product; its errata are recorded by themselves, not with it.
/// </summary>
internal sealed class PurchaseRules(IPurchaseRepository inner, ShoppingRepositories store, IActorSource actors)
    : RuledRepository<Purchase, PurchaseCriteria, long>(inner, actors), IPurchaseRepository
{
    public override async Task<bool> Insert(Purchase bean, Purchase? projection = null, CancellationToken cancellationToken = default)
    {
        if (bean.Amendments is { Count: > 0 })
        {
            throw new AccessDeniedException("An errata is inserted by itself, not with its purchase.");
        }

        // Without a buyer, it is whoever acts.
        bean.User ??= new User { Id = ShoppingPolicies.IdOf(Actor) };
        foreach (var item in bean.Items ?? [])
        {
            item.Price = await Prices.Of(store, item.Product?.Id, cancellationToken);
        }

        return await base.Insert(bean, projection, cancellationToken);
    }

    public override async Task<bool> Update(Purchase newBean, Purchase? oldBean = null, Purchase? projection = null,
        CancellationToken cancellationToken = default)
    {
        var written = Written(Purchase.Model, newBean, oldBean, projection);
        if (!Maintains && await AnyMade(store, new PurchaseCriteria().PurchaseId.Eq(newBean.Id), cancellationToken))
        {
            throw new AccessDeniedException("The purchase was already made: it does not change.");
        }

        if (written.Amendments is not null)
        {
            throw new AccessDeniedException("An errata is inserted by itself, not with its purchase.");
        }

        if (written.Items is not null)
        {
            var shape = (written.Items as ProjectionList<PurchaseItem>)?.Shape ?? written.Items.FirstOrDefault();
            foreach (var item in newBean.Items ?? [])
            {
                if (item.Id is null)
                {
                    item.Price = await Prices.Of(store, item.Product?.Id, cancellationToken);
                }
                else if (shape?.Price is not null)
                {
                    throw new AccessDeniedException("The price of an item comes from the product.");
                }
            }
        }

        return await base.Update(newBean, oldBean, projection, cancellationToken);
    }

    public override async Task<int> Delete(PurchaseCriteria criteria, CancellationToken cancellationToken = default)
    {
        if (!Maintains && await AnyMade(store, criteria, cancellationToken))
        {
            throw new AccessDeniedException("The purchase was already made: it is not deleted.");
        }

        return await base.Delete(criteria, cancellationToken);
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

/// <summary>The price of a product, as an item takes it.</summary>
internal static class Prices
{
    public static async Task<decimal?> Of(ShoppingRepositories store, long? productId, CancellationToken cancellationToken)
    {
        var product = productId is { } id
            ? await store.Products.FetchById(id, new Product { Price = Mark.Decimal }, cancellationToken)
            : null;
        return product?.Price ?? throw new InvalidRequestException($"No product {productId}.");
    }
}
