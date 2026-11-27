using Shopping.Domain.Purchases;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>The lines of the errata: written only with their errata.</summary>
public sealed class PurchaseAmendmentLineRepository(Database database)
    : SqlRepository<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria, long>(database, Mapping), IPurchaseAmendmentLineRepository
{
    public static readonly EntityMapping<PurchaseAmendmentLine> Mapping =
        EntityMapping.For(PurchaseAmendmentLine.Model, "purchase_amendment_line", "pl")
            .Key("id", generated: true)
            .Column("itemId", "item_id")
            .Column("price")
            .Column("amountBefore", "amount_before")
            .Column("amountAfter", "amount_after")
            .Reference("amendment", "amendment_id", () => PurchaseAmendmentRepository.Mapping)
            .Reference("product", "product_id", () => ProductRepository.Mapping)
            .Conditions<PurchaseAmendmentLineCriteria>(Conditions)
            .Ordering<PurchaseAmendmentLineOrderBy>(o => o switch
            {
                PurchaseAmendmentLineOrderBy.NewestFirst => [SortSpec.Desc("id")],
                _ => [SortSpec.Asc("id")],
            })
            .Build();

    private static ISqlFragment? Conditions(PurchaseAmendmentLineCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.PurchaseAmendmentLineId),
        t.Translate("amendment_id", criteria.PurchaseAmendmentId),
        t.Translate("item_id", criteria.ItemId),
        ExistsPurchaseOfUser(criteria, t),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);

    /// <summary>Lines whose errata are of purchases of the user asked for: through the errata, then the purchase.</summary>
    private static ISqlFragment? ExistsPurchaseOfUser(PurchaseAmendmentLineCriteria criteria, FilterContext t)
    {
        if (!FilterContext.Asks(criteria.UserId))
        {
            return null;
        }

        var amendment = t.NewAlias("pa");
        var purchase = t.NewAlias("pu");
        var onUser = t.Translate(Sql.Column(purchase, "user_id"), criteria.UserId);
        return Sql.Exists(w =>
        {
            w.Append("SELECT 1 FROM ").Table("purchase_amendment").Append(' ').Identifier(amendment)
                .Append(" JOIN ").Table("purchase").Append(' ').Identifier(purchase).Append(" ON ");
            Sql.Equal(Sql.Column(purchase, "id"), Sql.Column(amendment, "purchase_id")).Render(w);
            w.Append(" WHERE ");
            Sql.And([Sql.Equal(Sql.Column(amendment, "id"), t.Column("amendment_id")), onUser])!.Render(w);
        });
    }
}
