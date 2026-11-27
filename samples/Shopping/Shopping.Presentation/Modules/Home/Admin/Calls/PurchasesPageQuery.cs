using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Shopping.Presentation.Modules.Home.Calls;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Admin.Calls;

/// <summary>
/// A page of everyone's purchases, most recent first, optionally only those whose customer's name contains
/// <paramref name="customer"/>; with the customer, the items and whether each was corrected.
/// </summary>
public sealed class PurchasesPageQuery(ShoppingRepositories repositories, string? customer, int page, int pageSize)
    : ResourceQuery<PurchasesPage>
{
    public override Task<PurchasesPage> Run(CancellationToken cancellationToken = default)
    {
        var criteria = new PurchaseCriteria().WithOrderBy(PurchaseOrderBy.MostRecentPurchaseFirst);
        if (customer is not null)
        {
            criteria.CustomerName.Containing(customer);
        }

        return PurchasesPage.Load(
            repositories.Purchases,
            criteria,
            new Purchase
            {
                Id = Mark.Int64,
                BuyDate = Mark.Instant,
                User = new User { Name = Mark.String },
                Items = Mark.List(new PurchaseItem { Price = Mark.Decimal, Amount = Mark.Int32, Product = new Product { Name = Mark.String } }),
                Amendments = Mark.List(new PurchaseAmendment { Id = Mark.Int64 }),
            },
            page,
            pageSize,
            cancellationToken);
    }
}
