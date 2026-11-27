using Wdc.Domain.Tests;

namespace Wdc.Persistence.Tests;

/// <summary>Items of the test domain.</summary>
public sealed class PurchaseItemRepository(Database database)
    : SqlRepository<PurchaseItem, PurchaseItemCriteria, long>(database, Mapping)
{
    public static readonly EntityMapping<PurchaseItem> Mapping = EntityMapping.For(PurchaseItem.Model, "purchase_item", "pi")
        .Key("id", generated: true)
        .Column("amount")
        .Column("price")
        .Reference("purchase", "purchase_id", () => PurchaseRepository.Mapping)
        .Reference("product", "product_id", () => ProductRepository.Mapping)
        .Conditions<PurchaseItemCriteria>(Conditions)
        .Ordering<PurchaseItemOrderBy>(o => o switch
        {
            PurchaseItemOrderBy.MostExpensiveFirst => [SortSpec.Desc("price")],
            PurchaseItemOrderBy.LargestQuantityFirst => [SortSpec.Desc("amount")],
            _ => [SortSpec.Asc("id")],
        })
        .Build();

    private static ISqlFragment? Conditions(PurchaseItemCriteria criteria, FilterContext t) => Sql.And([
        t.Translate("id", criteria.PurchaseItemId),
        t.Translate("purchase_id", criteria.PurchaseId),
        t.Translate("amount", criteria.Amount),
        t.AnyOf(criteria.AnyOf, g => Conditions(g, t)),
    ]);
}
