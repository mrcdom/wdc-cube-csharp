using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// An item takes the price of its product, never one of its own. Once its purchase is made, no item enters it, and
/// its items do not change nor leave, but by maintenance.
/// </summary>
internal sealed class PurchaseItemRules(IPurchaseItemRepository inner, ShoppingRepositories store, IActorSource actors)
    : RuledRepository<PurchaseItem, PurchaseItemCriteria, long>(inner, actors), IPurchaseItemRepository
{
    public override async Task<bool> Insert(PurchaseItem bean, PurchaseItem? projection = null, CancellationToken cancellationToken = default)
    {
        if (bean.Purchase?.Id is not { } purchaseId)
        {
            throw new InvalidRequestException("An item needs its purchase.");
        }

        if (!Maintains && await PurchaseRules.AnyMade(store, new PurchaseCriteria().PurchaseId.Eq(purchaseId), cancellationToken))
        {
            throw new AccessDeniedException("The purchase was already made: no item enters it.");
        }

        bean.Price = await Prices.Of(store, bean.Product?.Id, cancellationToken);
        return await base.Insert(bean, projection, cancellationToken);
    }

    public override async Task<bool> Update(PurchaseItem newBean, PurchaseItem? oldBean = null, PurchaseItem? projection = null,
        CancellationToken cancellationToken = default)
    {
        // For every role, maintenance included.
        if (Written(PurchaseItem.Model, newBean, oldBean, projection).Price is not null)
        {
            throw new AccessDeniedException("The price of an item comes from the product.");
        }

        if (!Maintains && await AnyOfAPurchaseMade(new PurchaseItemCriteria().PurchaseItemId.Eq(newBean.Id), cancellationToken))
        {
            throw new AccessDeniedException("The purchase was already made: its items do not change.");
        }

        return await base.Update(newBean, oldBean, projection, cancellationToken);
    }

    public override async Task<int> Delete(PurchaseItemCriteria criteria, CancellationToken cancellationToken = default)
    {
        if (!Maintains && await AnyOfAPurchaseMade(criteria, cancellationToken))
        {
            throw new AccessDeniedException("The purchase was already made: its items cannot be removed.");
        }

        return await base.Delete(criteria, cancellationToken);
    }

    private async Task<bool> AnyOfAPurchaseMade(PurchaseItemCriteria criteria, CancellationToken cancellationToken)
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
}
