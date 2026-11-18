using Shopping.Domain.Purchases;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>A purchase is written with its items, in one transaction (the caller's, or one of its own).</summary>
public sealed class PurchaseRepository(Database database, PurchaseItemRepository items)
    : SqlRepository<Purchase, PurchaseCriteria, long>(database, ShoppingMappings.Purchase), IPurchaseRepository
{
    public override Task<bool> Insert(Purchase bean, Purchase? projection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        return Database.Required(async _ =>
        {
            if (!await base.Insert(bean, projection, cancellationToken))
            {
                return false;
            }

            foreach (var item in bean.Items ?? [])
            {
                item.Purchase = bean;
                await items.Insert(item, cancellationToken: cancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    public override async Task<int> Delete(PurchaseCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (!criteria.HasFilter)
        {
            throw new InvalidRequestException("delete of purchases without a filter is refused");
        }

        // Children first, in the same transaction.
        return await Database.Required(async _ =>
        {
            var ids = (await Fetch(criteria.WithProjection(Purchase.Model.KeyProjection()), cancellationToken: cancellationToken))
                .Select(p => p.Id).ToList();
            if (ids.Count == 0)
            {
                return 0;
            }

            await items.Delete(new PurchaseItemCriteria().PurchaseId.In(ids), cancellationToken);
            return await base.Delete(new PurchaseCriteria().PurchaseId.In(ids), cancellationToken);
        }, cancellationToken);
    }
}
