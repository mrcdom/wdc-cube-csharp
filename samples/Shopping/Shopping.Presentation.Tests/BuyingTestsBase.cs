using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Receipt;

namespace Shopping.Presentation.Tests;

/// <summary>Product, cart and receipt.</summary>
public abstract class BuyingTestsBase(ShoppingDatabaseFixture db)
{
    private static async Task<ProductPresenter> OpenProduct(Shop shop, string name)
    {
        var products = shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Products!;
        await shop.Do(() => products.OnOpenProduct(products.Products.Single(p => p.Name == name).Id));
        return shop.PresenterOf<ProductPresenter>(ProductKeys.Place);
    }

    [Fact]
    public async Task Product_ShowsTheDescriptionAsItems_AndTheAddressNamesIt()
    {
        var shop = await Shop.LoggedIn(db, "fulano");

        var product = await OpenProduct(shop, "Volleyball");

        Assert.Equal(["Volleyball, official size and weight.", "Warranty: against manufacturing defects.", "Origin: imported."],
            product.Scope.DescriptionItems);
        Assert.Equal("$45.30", product.Scope.PriceLabel);
        Assert.Equal($"home/product?pr-id={product.Scope.Id}", shop.History.Token);
        Assert.Same(product.Scope, shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Content);
    }

    [Fact]
    public async Task Product_ThatDoesNotExist_IsRefused_FromTheListAndFromTheAddress()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        var home = shop.PresenterOf<HomePresenter>(HomeKeys.Place);

        await shop.Do(() => home.Scope.Products!.OnOpenProduct(999_999));
        Assert.Equal(HomePresenter.ProductNotFound, shop.App.Scope.ErrorMessage);
        Assert.Equal("home", shop.History.Token);

        var deep = await Shop.Start(db, "home/product?pr-id=999999", shop.Session);
        Assert.Null(deep.Maybe<ProductPresenter>(ProductKeys.Place));
        Assert.Equal("home", deep.History.Token);
    }

    [Fact]
    public async Task AddingToTheCart_ValidatesTheQuantity_AndTheBadgeCountsQuantities()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        var product = await OpenProduct(shop, "Thread Seal Tape");

        await shop.Do(() => product.Scope.OnAddToCart(0));
        Assert.Equal(ProductPresenter.QuantityBelowOne, product.Scope.ErrorMessage);

        await shop.Do(() => product.Scope.OnAddToCart(2));
        var cart = shop.PresenterOf<CartPresenter>(CartKeys.Place);
        Assert.Equal(2, cart.Scope.Lines.Single().Quantity);
        Assert.Equal("$5.34", cart.Scope.TotalLabel);
        Assert.Equal("home/cart", shop.History.Token);

        product = await OpenProduct(shop, "Thread Seal Tape");
        await shop.Do(() => product.Scope.OnAddToCart(1)); // the same product again: its quantity grows
        Assert.Equal(3, shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.CartCount); // the badge follows the quantities
    }

    [Fact]
    public async Task Cart_QuantityBelowOneIsRefused_AndTheMessageGoesAway()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        await shop.Do(() => OpenProduct(shop, "Volleyball").ContinueWith(t => t.Result.Scope.OnAddToCart(1)).Unwrap());
        var cart = shop.PresenterOf<CartPresenter>(CartKeys.Place);
        var line = cart.Scope.Lines.Single();

        await shop.Do(() => line.OnChangeQuantity(0));
        Assert.Equal(CartMessages.QuantityBelowOne, cart.Scope.ErrorMessage);
        Assert.Equal(1, line.Quantity);

        shop.Time.Advance(CartPresenter.MessageDuration);
        await shop.Settle();
        Assert.Null(cart.Scope.ErrorMessage);

        await shop.Do(() => line.OnChangeQuantity(4));
        Assert.Same(line, cart.Scope.Lines.Single()); // the same line, updated in place
        Assert.Equal("$181.20", cart.Scope.TotalLabel);
    }

    [Fact]
    public async Task Cart_RemovingTheLastLine_GoesHome()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        var product = await OpenProduct(shop, "USB Flash Drive 2GB");
        await shop.Do(() => product.Scope.OnAddToCart(1));

        await shop.Do(shop.PresenterOf<CartPresenter>(CartKeys.Place).Scope.Lines.Single().OnRemove);

        Assert.Equal("home", shop.History.Token);
        Assert.Equal(0, shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.CartCount);
    }

    [Fact]
    public async Task Buying_RecordsThePurchase_ShowsTheReceipt_AndRefreshesHome()
    {
        var shop = await Shop.LoggedIn(db, "beotrano");
        var product = await OpenProduct(shop, "Italian Design Coffee Maker");
        await shop.Do(() => product.Scope.OnAddToCart(2));

        await shop.Do(shop.PresenterOf<CartPresenter>(CartKeys.Place).Scope.OnBuy);

        var receipt = shop.PresenterOf<ReceiptPresenter>(ReceiptKeys.Place);
        Assert.True(receipt.Scope.NotifySuccess);
        Assert.Equal([("Italian Design Coffee Maker", 2, "$199.99")], receipt.Scope.Lines.Select(l => (l.Description, l.Quantity, l.ValueLabel)));
        Assert.Equal("$399.98", receipt.Scope.TotalLabel);
        Assert.Equal("11/18/2026", receipt.Scope.DateLabel); // the application's clock
        Assert.StartsWith("home/receipt?rc-id=", shop.History.Token, StringComparison.Ordinal);

        var home = shop.PresenterOf<HomePresenter>(HomeKeys.Place);
        Assert.Equal(0, home.Scope.CartCount);
        Assert.Equal("$399.98", home.Scope.Purchases!.Purchases[0].TotalLabel);

        // The banner belongs to the trip from the cart: the same receipt opened by its address does not show it.
        var reopened = await Shop.Start(db, shop.History.Token, shop.Session);
        Assert.False(reopened.PresenterOf<ReceiptPresenter>(ReceiptKeys.Place).Scope.NotifySuccess);
    }

    [Fact]
    public async Task Buying_WithADoubleClick_RecordsOnePurchase()
    {
        var shop = await Shop.LoggedIn(db, "beotrano");
        var product = await OpenProduct(shop, "Volleyball");
        await shop.Do(() => product.Scope.OnAddToCart(1));
        var mine = new Domain.Purchases.PurchaseCriteria().UserId.Eq(shop.App.Subject!.Id);
        var before = await db.Store!.Repositories.Purchases.Count(mine, TestContext.Current.CancellationToken);

        var buy = shop.PresenterOf<CartPresenter>(CartKeys.Place).Scope.OnBuy;
        await shop.Do(() => Task.WhenAll(buy(), buy())); // the second click arrives while the first one is recording

        Assert.Equal(before + 1, await db.Store!.Repositories.Purchases.Count(mine, TestContext.Current.CancellationToken));
        Assert.StartsWith("home/receipt?rc-id=", shop.History.Token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Receipt_OfSomeoneElse_IsTheSameAsOneThatDoesNotExist()
    {
        var admin = await Shop.LoggedIn(db, "admin");
        var adminPurchase = admin.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Purchases!.Purchases[0].Id;
        var shop = await Shop.LoggedIn(db, "fulano");

        await shop.Do(() => shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Purchases!.OnOpenReceipt(adminPurchase));

        Assert.Equal(HomePresenter.ReceiptNotFound, shop.App.Scope.ErrorMessage);
        Assert.Null(shop.Maybe<ReceiptPresenter>(ReceiptKeys.Place));

        var deep = await Shop.Start(db, $"home/receipt?rc-id={adminPurchase}", shop.Session);
        Assert.Null(deep.Maybe<ReceiptPresenter>(ReceiptKeys.Place));
        Assert.Equal(HomePresenter.ReceiptNotFound, deep.App.Scope.ErrorMessage); // a message for the user, not the log's
    }

    [Fact]
    public async Task ErrorToast_GoesAwayOnItsOwn()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        await shop.Do(() => shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.Products!.OnOpenProduct(999_998));
        Assert.NotNull(shop.App.Scope.ErrorMessage);

        shop.Time.Advance(Modules.RootPresenter.ErrorToastDuration);
        await shop.Settle();

        Assert.Null(shop.App.Scope.ErrorMessage);
    }
}
