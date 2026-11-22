using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Receipt;

namespace Shopping.Presentation.Tests;

/// <summary>Home: the two panels and the history's pages.</summary>
public abstract class HomeTestsBase(ShoppingDatabaseFixture db)
{
    [Fact]
    public async Task ProductsPanel_ListsTheCatalogueByName_WithPrices()
    {
        var shop = await Shop.LoggedIn(db, "admin");
        var products = shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Products!;

        Assert.Equal(["Bola de vôlei", "Cafeteira design italiano", "Fita veda rosca", "Pen Drive 2GB"], products.Products.Select(p => p.Name));
        Assert.Equal("R$ 45,30", products.Products[0].PriceLabel);
        Assert.Equal("4 itens", products.CountLabel);
        Assert.Equal($"image/product/{products.Products[0].Id}.png", products.Products[0].ImageUrl);
    }

    [Fact]
    public async Task PurchasesPanel_MostRecentFirst_WithDateItemsAndTotal()
    {
        var shop = await Shop.LoggedIn(db, "admin");
        var purchases = shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Purchases!;

        Assert.Equal(
            [("03/04/2011", "Bola de vôlei, Fita veda rosca", "R$ 47,97"), ("01/01/2010", "Cafeteira design italiano", "R$ 200,00")],
            purchases.Purchases.Select(p => (p.DateLabel, p.ItemsLabel, p.TotalLabel)));
        Assert.Equal("2 compras", purchases.CountLabel);
        Assert.Equal("1 / 1", purchases.PageLabel);
        Assert.False(purchases.HasNext);
    }

    [Fact]
    public async Task PurchasesPanel_PagesGoToTheAddress_AndComeBackFromIt()
    {
        var shop = await Shop.LoggedIn(db, "beotrano");
        for (var i = 0; i < PurchasesPanel.PageSize + 2; i++)
        {
            await BuyOne(shop);
        }

        var purchases = shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Purchases!;
        Assert.True(purchases.HasNext);
        await shop.Do(purchases.OnNext);
        Assert.Equal("2 / 2", purchases.PageLabel);
        Assert.Equal(2, purchases.Purchases.Count);
        Assert.Equal("home?hm-page=2", shop.History.Token);

        var deep = await Shop.Start(db, "home?hm-page=99", shop.Session); // past the end: the last page that exists
        Assert.Equal("2 / 2", deep.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Purchases!.PageLabel);
        Assert.Equal(2, deep.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Purchases!.Purchases.Count);
    }

    private static async Task BuyOne(Shop shop)
    {
        var home = shop.PresenterOf<HomePresenter>(HomeKeys.Place);
        await shop.Do(() => home.Scope.Products!.OnOpenProduct(home.Scope.Products!.Products[2].Id));
        await shop.Do(() => shop.PresenterOf<ProductPresenter>(ProductKeys.Place).Scope.OnAddToCart(1));
        await shop.Do(shop.PresenterOf<CartPresenter>(CartKeys.Place).Scope.OnBuy);
        await shop.Do(shop.PresenterOf<ReceiptPresenter>(ReceiptKeys.Place).Scope.OnContinue);
    }
}
