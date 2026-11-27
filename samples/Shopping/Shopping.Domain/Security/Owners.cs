using Shopping.Domain.Purchases;
using Wdc.Domain;

namespace Shopping.Domain.Security;

/// <summary>Who owns a row, for the row checks: from what the row carries, or looked up in the trusted inside.</summary>
internal static class Owners
{
    /// <summary>The buyer of <paramref name="purchase"/>: its user, or the stored one when it carries only its key.</summary>
    public static async ValueTask<long?> OfPurchase(ShoppingRepositories store, Purchase? purchase, CancellationToken cancellationToken)
    {
        if (purchase?.User?.Id is { } owner)
        {
            return owner;
        }

        if (purchase?.Id is not { } id)
        {
            return null;
        }

        var stored = await store.Purchases.FetchById(id, new Purchase { User = new Users.User { Id = Mark.Int64 } }, cancellationToken);
        return stored?.User?.Id;
    }
}
