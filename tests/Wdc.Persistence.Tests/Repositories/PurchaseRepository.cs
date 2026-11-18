using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>Purchases of the test domain; <c>items</c> is filtered by <see cref="PurchaseItemRepository"/>'s conditions.</summary>
public sealed class PurchaseRepository(Database database) : SqlRepository<Purchase, PurchaseCriteria, long>(database, Mapping)
{
    public static readonly EntityMapping<Purchase> Mapping = EntityMapping.For(Purchase.Model, "purchase", "pu")
        .Key("id", generated: true)
        .Column("buyDate", "buy_date")
        .Reference("user", "user_id", () => UserRepository.Mapping)
        .Collection("items", "purchase_id", () => PurchaseItemRepository.Mapping)
        .Conditions<PurchaseCriteria>(Conditions)
        .Ordering<PurchaseOrderBy>(o => [SortSpec.Desc("buy_date")])
        .Build();

    private static ISqlFragment? Conditions(PurchaseCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.PurchaseId),
        t.Translate("user_id", criteria.UserId),
        t.Translate("buy_date", criteria.BuyDate),
        ExistsItemMatching(criteria, t),
    ]);

    /// <summary>Purchases that contain the product: the condition applies to their items.</summary>
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
