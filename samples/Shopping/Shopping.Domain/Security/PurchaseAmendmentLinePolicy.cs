using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>The lines of the errata: read by whoever reads the errata, recorded with it.</summary>
public static class PurchaseAmendmentLinePolicy
{
    private static readonly string[] s_fields = ["itemId", "price", "amountBefore", "amountAfter", "amendment", "product"];

    public static DataPolicy<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria> Build(ShoppingPolicies all, ShoppingRepositories store)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(store);
        return DataPolicy.For(PurchaseAmendmentLine.Model, () => new PurchaseAmendmentLineCriteria())
            .Reference("amendment", () => all.Amendments)
            .Reference("product", () => all.Products)
            .Criterion("purchaseAmendmentLineId", "id")
            .Criterion("purchaseAmendmentId", "amendment")
            .Criterion("userId", "amendment.purchase.user")
            .Grant(ShoppingPolicies.Super, g => g.Read(s_fields).Create(s_fields).Update(s_fields).Delete())
            .Grant([.. ShoppingPolicies.Admin, .. ShoppingPolicies.Manager], g => g.Read(s_fields).Create(s_fields))
            .Grant(ShoppingPolicies.Customer, g => g.Rows(new OwnAmendmentLines(store)).Read(s_fields))
            .Build();
    }
}

/// <summary>The lines of the errata of the actor's purchases.</summary>
internal sealed class OwnAmendmentLines(ShoppingRepositories store) : IRowScope<PurchaseAmendmentLine, PurchaseAmendmentLineCriteria>
{
    public void Restrict(PurchaseAmendmentLineCriteria group, Actor actor) => group.UserId.Eq(ShoppingPolicies.IdOf(actor));

    public async ValueTask<bool> Covers(PurchaseAmendmentLine row, Actor actor, CancellationToken cancellationToken)
    {
        var purchase = row.Amendment?.Purchase;
        if (purchase is null && row.Amendment?.Id is { } amendmentId)
        {
            purchase = (await store.Amendments.FetchById(amendmentId,
                new PurchaseAmendment { Purchase = new Purchase { User = new Users.User { Id = Mark.Int64 } } }, cancellationToken))?.Purchase;
        }

        return await Owners.OfPurchase(store, purchase, cancellationToken) == ShoppingPolicies.IdOf(actor);
    }

    public PurchaseAmendmentLine? Projection() =>
        new() { Amendment = new PurchaseAmendment { Purchase = new Purchase { User = new Users.User { Id = Mark.Int64 } } } };
}
