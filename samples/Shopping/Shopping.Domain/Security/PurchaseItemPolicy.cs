using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The items of the purchases, read by whoever reads the purchase. They are created with it (the customer's own, or
/// Admin's for anyone); only Super changes or removes one.
/// </summary>
public static class PurchaseItemPolicy
{
    private static readonly string[] s_fields = ["amount", "price", "purchase", "product"];

    public static DataPolicy<PurchaseItem, PurchaseItemCriteria> Build(ShoppingPolicies all, ShoppingRepositories store)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(store);
        return DataPolicy.For(PurchaseItem.Model, () => new PurchaseItemCriteria())
            .Reference("purchase", () => all.Purchases)
            .Reference("product", () => all.Products)
            .Criterion("purchaseItemId", "id")
            .Criterion("purchaseId", "purchase")
            .Criterion("productId", "product")
            .Criterion("userId", "purchase.user")
            .Orderings(c => c.OrderBy switch
            {
                PurchaseItemOrderBy.MostExpensiveFirst => ["price"],
                PurchaseItemOrderBy.LargestQuantityFirst => ["amount"],
                _ => [],
            })
            .Grant(ShoppingPolicies.Super, g => g.Read(s_fields).Create(s_fields).Update(s_fields).Delete())
            .Grant([.. ShoppingPolicies.Admin, .. ShoppingPolicies.Manager], g => g.Read(s_fields))
            .Grant(ShoppingPolicies.Admin, g => g.Create(s_fields))
            .Grant(ShoppingPolicies.Customer, g => g.Rows(new OwnItems(store)).Read(s_fields).Create(s_fields))
            .Build();
    }
}

/// <summary>The items of the actor's purchases; an item that carries only its purchase's key looks the owner up.</summary>
internal sealed class OwnItems(ShoppingRepositories store) : IRowScope<PurchaseItem, PurchaseItemCriteria>
{
    public void Restrict(PurchaseItemCriteria group, Actor actor) => group.UserId.Eq(ShoppingPolicies.IdOf(actor));

    public async ValueTask<bool> Covers(PurchaseItem row, Actor actor, CancellationToken cancellationToken) =>
        await Owners.OfPurchase(store, row.Purchase, cancellationToken) == ShoppingPolicies.IdOf(actor);

    public PurchaseItem? Projection() => new() { Purchase = new Purchase { User = new Users.User { Id = Mark.Int64 } } };
}
