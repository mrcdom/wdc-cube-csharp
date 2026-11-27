using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>
/// The errata of the purchases: Admin and Manager read and record them; a customer reads those of their own purchases,
/// without who corrected them; only Super changes or deletes one.
/// </summary>
public static class PurchaseAmendmentPolicy
{
    private static readonly string[] s_fields = ["date", "reason", "purchase", "author", "lines"];

    private static readonly string[] s_create = ["date", "reason", "purchase", "author", "lines"];

    public static DataPolicy<PurchaseAmendment, PurchaseAmendmentCriteria> Build(ShoppingPolicies all, ShoppingRepositories store)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(store);
        return DataPolicy.For(PurchaseAmendment.Model, () => new PurchaseAmendmentCriteria())
            .Reference("purchase", () => all.Purchases)
            .Reference("author", () => all.Users)
            .Collection("lines", inverse: "amendment", () => all.AmendmentLines)
            .Criterion("purchaseAmendmentId", "id")
            .Criterion("purchaseId", "purchase")
            .Criterion("authorId", "author")
            .Criterion("userId", "purchase.user")
            .Grant(ShoppingPolicies.Super, g => g.Read(s_fields).Create(s_create).Update(s_create).Delete())
            .Grant([.. ShoppingPolicies.Admin, .. ShoppingPolicies.Manager], g => g.Read(s_fields).Create(s_create))
            .Grant(ShoppingPolicies.Customer, g => g.Rows(new OwnAmendments(store)).Read("date", "reason", "purchase", "lines"))
            .Build();
    }
}

/// <summary>The errata of the actor's purchases.</summary>
internal sealed class OwnAmendments(ShoppingRepositories store) : IRowScope<PurchaseAmendment, PurchaseAmendmentCriteria>
{
    public void Restrict(PurchaseAmendmentCriteria group, Actor actor) => group.UserId.Eq(ShoppingPolicies.IdOf(actor));

    public async ValueTask<bool> Covers(PurchaseAmendment row, Actor actor, CancellationToken cancellationToken) =>
        await Owners.OfPurchase(store, row.Purchase, cancellationToken) == ShoppingPolicies.IdOf(actor);

    public PurchaseAmendment? Projection() => new() { Purchase = new Purchase { User = new Users.User { Id = Mark.Int64 } } };
}
