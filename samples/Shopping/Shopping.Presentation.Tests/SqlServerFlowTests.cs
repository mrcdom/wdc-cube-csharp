using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Receipt;

namespace Shopping.Presentation.Tests;

/// <summary>The main flow on SQL Server: the demo's DDL, data and queries on the other dialect.</summary>
public sealed class SqlServerFlowTests(ShoppingSqlServerFixture db) : IClassFixture<ShoppingSqlServerFixture>
{
    [Fact]
    public async Task LoginCatalogueHistoryBuyAndReceipt()
    {
        var shop = await Shop.LoggedIn(db, "admin");
        var home = shop.PresenterOf<HomePresenter>(HomeKeys.Place);

        Assert.Equal("Hello, John Smith", home.Scope.Greeting);
        Assert.Equal(["Italian Design Coffee Maker", "Thread Seal Tape", "USB Flash Drive 2GB", "Volleyball"],
            home.Scope.Products!.Products.Select(p => p.Name));
        Assert.Equal(["$47.97", "$200.00"], home.Scope.Purchases!.Purchases.Select(p => p.TotalLabel));

        var ball = home.Scope.Products!.Products.Single(p => p.Name == "Volleyball").Id;
        await shop.Do(() => home.Scope.Products!.OnOpenProduct(ball));
        var product = shop.PresenterOf<ProductPresenter>(ProductKeys.Place);
        Assert.Equal(3, product.Scope.DescriptionItems.Count);

        await shop.Do(() => product.Scope.OnAddToCart(3));
        await shop.Do(shop.PresenterOf<CartPresenter>(CartKeys.Place).Scope.OnBuy);

        var receipt = shop.PresenterOf<ReceiptPresenter>(ReceiptKeys.Place);
        Assert.Equal("$135.90", receipt.Scope.TotalLabel);
        Assert.Equal("$135.90", home.Scope.Purchases!.Purchases[0].TotalLabel);
        Assert.Equal(2, await db.Store!.Repositories.Purchases.Count( // the 2011 demo purchase also has the ball
            new Domain.Purchases.PurchaseCriteria().ProductId.Eq(ball).UserId.Eq(shop.App.Subject!.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProductImages_AreStoredAndRead()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var products = await db.Store!.Repositories.Products.Fetch(new Domain.Products.ProductCriteria(), cancellationToken: TestContext.Current.CancellationToken);

        foreach (var product in products)
        {
            var image = await db.Store.Repositories.Products.FetchImage(product.Id!.Value, TestContext.Current.CancellationToken);
            Assert.True(image is { Length: > 1000 } && image[1] == (byte)'P' && image[2] == (byte)'N' && image[3] == (byte)'G');
        }
    }
}
