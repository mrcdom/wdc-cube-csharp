using Shopping.Domain.Purchases;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>The items of the purchases: their table and their criteria; written alone or with their purchase.</summary>
public sealed class PurchaseItemRepository(Database database)
    : SqlRepository<PurchaseItem, PurchaseItemCriteria, long>(database, Mapping), IPurchaseItemRepository
{
    /// <summary>
    /// How an item is stored; its relations point at the purchase's and the product's mappings, and a purchase's
    /// <c>items</c> are filtered here, by <see cref="Conditions"/>.
    /// </summary>
    public static readonly EntityMapping<PurchaseItem> Mapping = EntityMapping.For(PurchaseItem.Model, "purchase_item", "pi")
        .Key("id", generated: true)
        .Column("amount")
        .Column("price")
        .Reference("purchase", "purchase_id", () => PurchaseRepository.Mapping)
        .Reference("product", "product_id", () => ProductRepository.Mapping)
        .Conditions<PurchaseItemCriteria>(Conditions)
        .Ordering<PurchaseItemOrderBy>(o => o switch
        {
            PurchaseItemOrderBy.NewestFirst => [SortSpec.Desc("id")],
            PurchaseItemOrderBy.MostExpensiveFirst => [SortSpec.Desc("price")],
            PurchaseItemOrderBy.LargestQuantityFirst => [SortSpec.Desc("amount")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    public override Task<bool> Insert(PurchaseItem bean, PurchaseItem? projection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        if (bean.Amount is not > 0)
        {
            throw new InvalidRequestException("the item's amount must be greater than zero");
        }

        return base.Insert(bean, projection, cancellationToken);
    }

    /// <summary>The item's criteria on its table.</summary>
    private static ISqlFragment? Conditions(PurchaseItemCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.PurchaseItemId),
        t.Translate("purchase_id", criteria.PurchaseId),
        t.Translate("product_id", criteria.ProductId),
        t.Translate("amount", criteria.Amount),
        t.Translate("price", criteria.Price),
        ExistsPurchaseOfUser(criteria, t),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);

    /// <summary>
    /// Items whose purchases belong to the user asked for. The user owns the purchase, not the item: the condition
    /// applies to the purchase, correlated by the foreign key.
    /// </summary>
    private static ISqlFragment? ExistsPurchaseOfUser(PurchaseItemCriteria criteria, FilterContext t)
    {
        if (!FilterContext.Asks(criteria.UserId))
        {
            return null;
        }

        var purchase = t.NewAlias("pu");
        var onUser = t.Translate(Sql.Column(purchase, "user_id"), criteria.UserId);
        return Sql.Exists(w =>
        {
            w.Append("SELECT 1 FROM ").Table("purchase").Append(' ').Identifier(purchase).Append(" WHERE ");
            Sql.And([Sql.Equal(Sql.Column(purchase, "id"), t.Column("purchase_id")), onUser])!.Render(w);
        });
    }
}
