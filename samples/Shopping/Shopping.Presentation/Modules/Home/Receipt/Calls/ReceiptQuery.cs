using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Presentation.Infra;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Receipt.Calls;

/// <summary>
/// A purchase of the user, for the receipt: date, items and corrections. Someone else's is the same as one that does
/// not exist: the purchase is looked up by its key and its owner together, never only by key.
/// </summary>
/// <exception cref="PurchaseNotFoundException">No such purchase of this user.</exception>
public sealed class ReceiptQuery(ShoppingRepositories repositories, long purchaseId, long userId) : ResourceQuery<Purchase>
{
    public override async Task<Purchase> Run(CancellationToken cancellationToken = default)
    {
        var criteria = new PurchaseCriteria()
            .PurchaseId.Eq(purchaseId)
            .UserId.Eq(userId)
            .WithProjection(new Purchase
            {
                Id = Mark.Int64,
                BuyDate = Mark.Instant,
                Items = Mark.List(
                    new PurchaseItem { Id = Mark.Int64, Price = Mark.Decimal, Amount = Mark.Int32, Product = new Product { Name = Mark.String } },
                    new PurchaseItemCriteria().WithOrderBy(PurchaseItemOrderBy.OldestFirst)),
                Amendments = Mark.List(
                    new PurchaseAmendment { Date = Mark.Instant, Reason = Mark.String },
                    new PurchaseAmendmentCriteria().WithOrderBy(PurchaseAmendmentOrderBy.OldestFirst)),
            });
        return (await repositories.Purchases.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault()
               ?? throw new PurchaseNotFoundException(purchaseId);
    }
}
