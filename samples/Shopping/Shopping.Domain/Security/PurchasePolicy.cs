using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The purchases: a customer reads and makes their own; Admin reads every one and buys for anyone; only Super changes
/// or deletes one (that a purchase made does not change is a rule of <see cref="ShoppingRules"/>, and Super's
/// exemption with it).
/// </summary>
public static class PurchasePolicy
{
    private static readonly string[] s_read = ["buyDate", "user", "items", "amendments"];

    private static readonly string[] s_create = ["buyDate", "user", "items"];

    public static DataPolicy<Purchase, PurchaseCriteria> Build(ShoppingPolicies all)
    {
        ArgumentNullException.ThrowIfNull(all);
        return DataPolicy.For(Purchase.Model, () => new PurchaseCriteria())
            .Reference("user", () => all.Users)
            .Collection("items", inverse: "purchase", () => all.Items)
            .Collection("amendments", inverse: "purchase", () => all.Amendments)
            .Criterion("purchaseId", "id")
            .Criterion("userId", "user")
            .Criterion("productId", "items.product")
            .Criterion("customerName", "user.name")
            .Orderings(c => c.OrderBy is PurchaseOrderBy.MostRecentPurchaseFirst or PurchaseOrderBy.EarliestPurchaseFirst ? ["buyDate"] : [])
            .Grant(ShoppingPolicies.Super, g => g.Read(s_read).Create(s_create).Update(s_create).Delete())
            .Grant([.. ShoppingPolicies.Admin, .. ShoppingPolicies.Manager], g => g.Read(s_read))
            .Grant(ShoppingPolicies.Admin, g => g.Create(s_create))
            .Grant(ShoppingPolicies.Customer, g => g.Rows(OwnPurchases.Scope).Read(s_read).Create(s_create))
            .Build();
    }
}

/// <summary>The purchases the actor made.</summary>
internal sealed class OwnPurchases : IRowScope<Purchase, PurchaseCriteria>
{
    public static readonly OwnPurchases Scope = new();

    public void Restrict(PurchaseCriteria group, Actor actor) => group.UserId.Eq(ShoppingPolicies.IdOf(actor));

    public ValueTask<bool> Covers(Purchase row, Actor actor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(row.User?.Id == ShoppingPolicies.IdOf(actor));

    public Purchase? Projection() => new() { User = new Users.User { Id = Mark.Int64 } };
}
