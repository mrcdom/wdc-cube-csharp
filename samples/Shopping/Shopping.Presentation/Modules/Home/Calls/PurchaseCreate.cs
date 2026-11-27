using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Calls;

/// <summary>Records a purchase with its items, in one transaction.</summary>
/// <returns>The purchase id.</returns>
public sealed class PurchaseCreate(ShoppingRepositories repositories, long userId, DateTimeOffset buyDate,
    IReadOnlyList<CartLine> lines) : ResourceCreate<long>(repositories.Transactions)
{
    protected override async Task<long> Create(CancellationToken cancellationToken)
    {
        var purchase = new Purchase
        {
            User = new User { Id = userId },
            BuyDate = buyDate,
            Items =
            [
                .. lines.Select(l => new PurchaseItem
                {
                    Product = new Product { Id = l.ProductId },
                    Price = l.Price,
                    Amount = l.Quantity,
                }),
            ],
        };

        if (!await repositories.Purchases.Insert(purchase, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException("The purchase was not recorded.");
        }

        return purchase.Id!.Value;
    }
}
