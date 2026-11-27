using Shopping.Domain.Purchases;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>
/// A purchase is written with its items in one transaction, by the framework's cascade: its items
/// belong to it — they are inserted with it, synchronised on update when the projection marks them, and deleted with it.
/// </summary>
public sealed class PurchaseRepository(Database database)
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
        .Collection("items", "purchase_id", () => PurchaseItemRepository.Mapping, cascadeDelete: true) // resolved lazily
        .Collection("amendments", "purchase_id", () => PurchaseAmendmentRepository.Mapping, cascadeDelete: true)
        .Conditions<PurchaseCriteria>(Conditions)
        .Ordering<PurchaseOrderBy>(o => o switch
        {
            PurchaseOrderBy.NewestFirst => [SortSpec.Desc("id")],
            PurchaseOrderBy.MostRecentPurchaseFirst => [SortSpec.Desc("buy_date"), SortSpec.Desc("id")],
            PurchaseOrderBy.EarliestPurchaseFirst => [SortSpec.Asc("buy_date")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    /// <summary>A purchase is made when it is created (the cart is not stored): it comes with its date.</summary>
    public override Task<bool> Insert(Purchase bean, Purchase? projection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        if (bean.BuyDate is null)
        {
            throw new InvalidRequestException("a purchase needs its date of purchase");
        }

        return base.Insert(bean, projection, cancellationToken);
    }

    /// <summary>The purchase's criteria on its table.</summary>
    private static ISqlFragment? Conditions(PurchaseCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.PurchaseId),
        t.Translate("user_id", criteria.UserId),
        t.Translate("buy_date", criteria.BuyDate),
        ExistsItemMatching(criteria, t),
        ExistsUserNamed(criteria, t),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);

    /// <summary>Purchases of users whose name matches: the name is a field of the user, correlated by the foreign key.</summary>
    private static ISqlFragment? ExistsUserNamed(PurchaseCriteria criteria, FilterContext t)
    {
        if (!FilterContext.Asks(criteria.CustomerName))
        {
            return null;
        }

        var user = t.NewAlias("us");
        var onName = t.Translate(Sql.Column(user, "name"), criteria.CustomerName);
        return Sql.Exists(w =>
        {
            w.Append("SELECT 1 FROM ").Table("user_account").Append(' ').Identifier(user).Append(" WHERE ");
            Sql.And([Sql.Equal(Sql.Column(user, "id"), t.Column("user_id")), onName])!.Render(w);
        });
    }

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
