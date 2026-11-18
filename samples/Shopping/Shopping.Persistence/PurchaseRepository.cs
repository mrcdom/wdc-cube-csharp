using Shopping.Domain.Purchases;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>A purchase is written with its items, in one transaction (the caller's, or one of its own).</summary>
public sealed class PurchaseRepository(Database database, PurchaseItemRepository items)
    : SqlRepository<Purchase, PurchaseCriteria, long>(database, Mapping), IPurchaseRepository
{
    /// <summary>
    /// How a purchase is stored. Its relations point at the other repositories' mappings, so a criteria carried by
    /// the projection of <c>items</c> is translated by <see cref="PurchaseItemRepository"/>'s own conditions.
    /// </summary>
    public static readonly EntityMapping<Purchase> Mapping = EntityMapping.For(Purchase.Model, "purchase", "pu")
        .Key("id", generated: true)
        .Column("buyDate", "buy_date")
        .Reference("user", "user_id", () => UserRepository.Mapping)
        .Collection("items", "purchase_id", () => PurchaseItemRepository.Mapping) // mutual reference: resolved lazily
        .Conditions<PurchaseCriteria>(Conditions)
        .Ordering<PurchaseOrderBy>(o => o switch
        {
            PurchaseOrderBy.NewestFirst => [SortSpec.Desc("id")],
            PurchaseOrderBy.MostRecentPurchaseFirst => [SortSpec.Desc("buy_date"), SortSpec.Desc("id")],
            PurchaseOrderBy.EarliestPurchaseFirst => [SortSpec.Asc("buy_date")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

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

    /// <summary>The purchase's criteria on its table.</summary>
    private static ISqlFragment? Conditions(PurchaseCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.PurchaseId),
        t.Translate("user_id", criteria.UserId),
        t.Translate("buy_date", criteria.BuyDate),
        ExistsItemMatching(criteria, t),
    ]);

    /// <summary>
    /// Purchases that contain the product asked for. The product is not a column of the purchase but of its items:
    /// the criterion is a field like the others; only where the condition applies changes.
    /// </summary>
    private static ISqlFragment? ExistsItemMatching(PurchaseCriteria criteria, FilterContext t)
    {
        if (!FilterContext.Asks(criteria.ProductId))
        {
            return null;
        }

        var item = t.NewAlias("pi");
        var onProduct = t.Translate(Sql.Column(item, "product_id"), criteria.ProductId);
        return Sql.Exists(w =>
        {
            w.Append("SELECT 1 FROM ").Table("purchase_item").Append(' ').Identifier(item).Append(" WHERE ");
            Sql.And([Sql.Equal(Sql.Column(item, "purchase_id"), t.Column("id")), onProduct])!.Render(w);
        });
    }
}
