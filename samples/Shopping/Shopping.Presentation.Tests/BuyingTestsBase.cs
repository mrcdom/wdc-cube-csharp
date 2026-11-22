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

        var product = await OpenProduct(shop, "Bola de vôlei");

        Assert.Equal(["Tamanho e peso oficiais.", "Garantia contra defeito de fabricação.", "Revestimento sintético."],
            product.Scope.DescriptionItems);
        Assert.Equal("R$ 45,30", product.Scope.PriceLabel);
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
        var product = await OpenProduct(shop, "Fita veda rosca");

        await shop.Do(() => product.Scope.OnAddToCart(0));
        Assert.Equal(ProductPresenter.QuantityBelowOne, product.Scope.ErrorMessage);

        await shop.Do(() => product.Scope.OnAddToCart(2));
        var cart = shop.PresenterOf<CartPresenter>(CartKeys.Place);
        Assert.Equal(2, cart.Scope.Lines.Single().Quantity);
        Assert.Equal("R$ 5,34", cart.Scope.TotalLabel);
        Assert.Equal("home/cart", shop.History.Token);

        product = await OpenProduct(shop, "Fita veda rosca");
        await shop.Do(() => product.Scope.OnAddToCart(1)); // the same product again: its quantity grows
        Assert.Equal(3, shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.CartCount); // the badge follows the quantities
    }

    [Fact]
    public async Task Cart_QuantityBelowOneIsRefused_AndTheMessageGoesAway()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        await shop.Do(() => OpenProduct(shop, "Bola de vôlei").ContinueWith(t => t.Result.Scope.OnAddToCart(1)).Unwrap());
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
        Assert.Equal("R$ 181,20", cart.Scope.TotalLabel);
    }

    [Fact]
    public async Task Cart_RemovingTheLastLine_GoesHome()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        var product = await OpenProduct(shop, "Pen Drive 2GB");
        await shop.Do(() => product.Scope.OnAddToCart(1));

        await shop.Do(shop.PresenterOf<CartPresenter>(CartKeys.Place).Scope.Lines.Single().OnRemove);

        Assert.Equal("home", shop.History.Token);
        Assert.Equal(0, shop.PresenterOf<HomePresenter>(HomeKeys.Place).Scope.CartCount);
    }

    [Fact]
    public async Task Buying_RecordsThePurchase_ShowsTheReceipt_AndRefreshesHome()
    {
        var shop = await Shop.LoggedIn(db, "beotrano");
        var product = await OpenProduct(shop, "Cafeteira design italiano");
        await shop.Do(() => product.Scope.OnAddToCart(2));

        await shop.Do(shop.PresenterOf<CartPresenter>(CartKeys.Place).Scope.OnBuy);

        var receipt = shop.PresenterOf<ReceiptPresenter>(ReceiptKeys.Place);
        Assert.True(receipt.Scope.NotifySuccess);
        Assert.Equal([("Cafeteira design italiano", 2, "R$ 199,99")], receipt.Scope.Lines.Select(l => (l.Description, l.Quantity, l.ValueLabel)));
        Assert.Equal("R$ 399,98", receipt.Scope.TotalLabel);
        Assert.Equal("18/11/2026", receipt.Scope.DateLabel); // the application's clock
        Assert.StartsWith("home/receipt?rc-id=", shop.History.Token, StringComparison.Ordinal);

        var home = shop.PresenterOf<HomePresenter>(HomeKeys.Place);
        Assert.Equal(0, home.Scope.CartCount);
        Assert.Equal("R$ 399,98", home.Scope.Purchases!.Purchases[0].TotalLabel);

        // The banner belongs to the trip from the cart: the same receipt opened by its address does not show it.
        var reopened = await Shop.Start(db, shop.History.Token, shop.Session);
        Assert.False(reopened.PresenterOf<ReceiptPresenter>(ReceiptKeys.Place).Scope.NotifySuccess);
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
