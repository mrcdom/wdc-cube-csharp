using Shopping.Domain.Purchases;
using Wdc.Domain;
using Wdc.Persistence;

namespace Shopping.Persistence;

/// <summary>The errata of the purchases: inserted with their lines, deleted with their purchase.</summary>
public sealed class PurchaseAmendmentRepository(Database database)
    : SqlRepository<PurchaseAmendment, PurchaseAmendmentCriteria, long>(database, Mapping), IPurchaseAmendmentRepository
{
    public static readonly EntityMapping<PurchaseAmendment> Mapping = EntityMapping.For(PurchaseAmendment.Model, "purchase_amendment", "pa")
        .Key("id", generated: true)
        .Column("date", "amend_date")
        .Column("reason")
        .Reference("purchase", "purchase_id", () => PurchaseRepository.Mapping)
        .Reference("author", "author_id", () => UserRepository.Mapping)
        .Collection("lines", "amendment_id", () => PurchaseAmendmentLineRepository.Mapping, cascadeDelete: true)
        .Conditions<PurchaseAmendmentCriteria>(Conditions)
        .Ordering<PurchaseAmendmentOrderBy>(o => o switch
        {
            PurchaseAmendmentOrderBy.NewestFirst => [SortSpec.Desc("id")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    /// <summary>
    /// An errata is written whole — its own fields and its lines, whatever projection came — so its record never
    /// lacks what it corrected.
    /// </summary>
    public override Task<bool> Insert(PurchaseAmendment bean, PurchaseAmendment? projection = null,
        CancellationToken cancellationToken = default) =>
        base.Insert(bean, null, cancellationToken);

    private static ISqlFragment? Conditions(PurchaseAmendmentCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.PurchaseAmendmentId),
        t.Translate("purchase_id", criteria.PurchaseId),
        t.Translate("author_id", criteria.AuthorId),
        t.Translate("amend_date", criteria.Date),
        ExistsPurchaseOfUser(criteria, t),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);

    /// <summary>Errata whose purchases belong to the user asked for (the user owns the purchase).</summary>
    private static ISqlFragment? ExistsPurchaseOfUser(PurchaseAmendmentCriteria criteria, FilterContext t)
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
