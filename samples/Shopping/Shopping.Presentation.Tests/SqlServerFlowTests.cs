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
        var home = shop.PresenterOf<HomePresenter>(Places.Home);

        Assert.Equal("Olá, João da Silva", home.Scope.Greeting);
        Assert.Equal(["Bola de vôlei", "Cafeteira design italiano", "Fita veda rosca", "Pen Drive 2GB"],
            home.Scope.Products!.Products.Select(p => p.Name));
        Assert.Equal(["R$ 47,97", "R$ 200,00"], home.Scope.Purchases!.Purchases.Select(p => p.TotalLabel));

        await shop.Do(() => home.Scope.Products!.OnOpenProduct(home.Scope.Products!.Products[0].Id));
        var product = shop.PresenterOf<ProductPresenter>(Places.Product);
        Assert.Equal(3, product.Scope.DescriptionItems.Count);

        await shop.Do(() => product.Scope.OnAddToCart(3));
        await shop.Do(shop.PresenterOf<CartPresenter>(Places.Cart).Scope.OnBuy);

        var receipt = shop.PresenterOf<ReceiptPresenter>(Places.Receipt);
        Assert.Equal("R$ 135,90", receipt.Scope.TotalLabel);
        Assert.Equal("R$ 135,90", home.Scope.Purchases!.Purchases[0].TotalLabel);
        Assert.Equal(2, await db.Store!.Repositories.Purchases.Count( // the 2011 demo purchase also has the ball
            new Domain.Purchases.PurchaseCriteria().ProductId.Eq(home.Scope.Products!.Products[0].Id).UserId.Eq(shop.App.Subject!.Id), TestContext.Current.CancellationToken));
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
