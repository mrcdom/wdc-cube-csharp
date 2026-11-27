using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Presentation.Modules.Home.Calls;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Purchases.Calls;

/// <summary>A page of the user's own purchases, most recent first, with their items for the cards.</summary>
public sealed class MyPurchasesPageQuery(ShoppingRepositories repositories, long userId, int page, int pageSize)
    : ResourceQuery<PurchasesPage>
{
    public override Task<PurchasesPage> Run(CancellationToken cancellationToken = default) =>
        PurchasesPage.Load(
            repositories.Purchases,
            new PurchaseCriteria().UserId.Eq(userId).WithOrderBy(PurchaseOrderBy.MostRecentPurchaseFirst),
            new Purchase
            {
                Id = Mark.Int64,
                BuyDate = Mark.Instant,
                Items = Mark.List(new PurchaseItem { Price = Mark.Decimal, Amount = Mark.Int32, Product = new Product { Name = Mark.String } }),
            },
            page,
            pageSize,
            cancellationToken);
}
