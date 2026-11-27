using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Shopping.Presentation.Infra;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Admin.Amend.Calls;

/// <summary>A purchase as the correction screen shows it: customer, items, and the corrections already made.</summary>
/// <exception cref="PurchaseNotFoundException">No such purchase.</exception>
public sealed class AmendablePurchaseQuery(ShoppingRepositories repositories, long purchaseId) : ResourceQuery<Purchase>
{
    public override async Task<Purchase> Run(CancellationToken cancellationToken = default)
    {
        var criteria = new PurchaseCriteria()
            .PurchaseId.Eq(purchaseId)
            .WithProjection(new Purchase
            {
                Id = Mark.Int64,
                BuyDate = Mark.Instant,
                User = new User { Name = Mark.String },
                Items = Mark.List(
                    new PurchaseItem { Id = Mark.Int64, Price = Mark.Decimal, Amount = Mark.Int32, Product = new Product { Name = Mark.String } },
                    new PurchaseItemCriteria().WithOrderBy(PurchaseItemOrderBy.OldestFirst)),
                Amendments = Mark.List(
                    new PurchaseAmendment
                    {
                        Date = Mark.Instant,
                        Reason = Mark.String,
                        Author = new User { Name = Mark.String },
                        Lines = Mark.List(
                            new PurchaseAmendmentLine { AmountBefore = Mark.Int32, AmountAfter = Mark.Int32, Product = new Product { Name = Mark.String } },
                            new PurchaseAmendmentLineCriteria().WithOrderBy(PurchaseAmendmentLineOrderBy.OldestFirst)),
                    },
                    new PurchaseAmendmentCriteria().WithOrderBy(PurchaseAmendmentOrderBy.OldestFirst)),
            });
        return (await repositories.Purchases.Fetch(criteria, limit: 1, cancellationToken: cancellationToken)).FirstOrDefault()
               ?? throw new PurchaseNotFoundException(purchaseId);
    }
}
