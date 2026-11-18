using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Receipt;
using Shopping.Presentation.Modules.Login;

namespace Shopping.Presentation.Tests;

/// <summary>Login, guard and session (F5).</summary>
public sealed class SessionTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    [Fact]
    public async Task WithoutASession_EveryRestrictedAddressLandsOnLogin()
    {
        foreach (var address in new[] { "", "home", "home/cart", "home/product?pr-id=1", "home/receipt?rc-id=1" })
        {
            var shop = await Shop.Start(db, address);

            Assert.NotNull(shop.Maybe<LoginPresenter>(Places.Login));
            Assert.Null(shop.Maybe<HomePresenter>(Places.Home));
            Assert.Equal("login", shop.History.Token);
        }
    }

    [Fact]
    public async Task WrongCredentials_StayOnLoginWithTheMessage()
    {
        var shop = await Shop.Start(db);

        await shop.Login("admin", "wrong");

        var login = shop.PresenterOf<LoginPresenter>(Places.Login);
        Assert.Equal(LoginPresenter.NotRecognized, login.Scope.ErrorMessage);
        Assert.False(login.Scope.Loading);
        Assert.Null(shop.App.Subject);
    }

    [Fact]
    public async Task RightCredentials_OpenTheSessionAndGoHome()
    {
        var shop = await Shop.Start(db);

        await shop.Login("admin", "admin");

        var home = shop.PresenterOf<HomePresenter>(Places.Home);
        Assert.Equal("Olá, João da Silva", home.Scope.Greeting);
        Assert.Equal("home", shop.History.Token);
        Assert.NotNull(await shop.Session.Get(SubjectSession.Key));
    }

    [Fact]
    public async Task Reload_ComesBackLoggedIn_AtTheSameAddress()
    {
        var first = await Shop.LoggedIn(db, "fulano");

        var reloaded = await Shop.Start(db, "home", first.Session); // F5: same tab, same session storage

        Assert.Equal("Olá, Fulano de Tal", reloaded.PresenterOf<HomePresenter>(Places.Home).Scope.Greeting);
    }

    [Fact]
    public async Task Exit_ClosesTheSessionAndGoesToLogin()
    {
        var shop = await Shop.LoggedIn(db, "beotrano");

        await shop.Do(shop.PresenterOf<HomePresenter>(Places.Home).Scope.OnExit);

        Assert.Null(shop.App.Subject);
        Assert.Null(await shop.Session.Get(SubjectSession.Key));
        Assert.Equal("login", shop.History.Token);
    }
}

/// <summary>Home: the two panels and the history's pages.</summary>
public sealed class HomeTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    [Fact]
    public async Task ProductsPanel_ListsTheCatalogueByName_WithPrices()
    {
        var shop = await Shop.LoggedIn(db, "admin");
        var products = shop.PresenterOf<HomePresenter>(Places.Home).Scope.Products!;

        Assert.Equal(["Bola de vôlei", "Cafeteira design italiano", "Fita veda rosca", "Pen Drive 2GB"], products.Products.Select(p => p.Name));
        Assert.Equal("R$ 45,30", products.Products[0].PriceLabel);
        Assert.Equal("4 itens", products.CountLabel);
        Assert.Equal($"image/product/{products.Products[0].Id}.png", products.Products[0].ImageUrl);
    }

    [Fact]
    public async Task PurchasesPanel_MostRecentFirst_WithDateItemsAndTotal()
    {
        var shop = await Shop.LoggedIn(db, "admin");
        var purchases = shop.PresenterOf<HomePresenter>(Places.Home).Scope.Purchases!;

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

        var purchases = shop.PresenterOf<HomePresenter>(Places.Home).Scope.Purchases!;
        Assert.True(purchases.HasNext);
        await shop.Do(purchases.OnNext);
        Assert.Equal("2 / 2", purchases.PageLabel);
        Assert.Equal(2, purchases.Purchases.Count);
        Assert.Equal("home?hm-page=2", shop.History.Token);

        var deep = await Shop.Start(db, "home?hm-page=99", shop.Session); // past the end: the last page that exists
        Assert.Equal("2 / 2", deep.PresenterOf<HomePresenter>(Places.Home).Scope.Purchases!.PageLabel);
        Assert.Equal(2, deep.PresenterOf<HomePresenter>(Places.Home).Scope.Purchases!.Purchases.Count);
    }

    private static async Task BuyOne(Shop shop)
    {
        var home = shop.PresenterOf<HomePresenter>(Places.Home);
        await shop.Do(() => home.Scope.Products!.OnOpenProduct(home.Scope.Products!.Products[2].Id));
        await shop.Do(() => shop.PresenterOf<ProductPresenter>(Places.Product).Scope.OnAddToCart(1));
        await shop.Do(shop.PresenterOf<CartPresenter>(Places.Cart).Scope.OnBuy);
        await shop.Do(shop.PresenterOf<ReceiptPresenter>(Places.Receipt).Scope.OnContinue);
    }
}

internal static class PurchasesPanel
{
    public const int PageSize = Modules.Home.Purchases.PurchasesPanelPresenter.PageSize;
}

/// <summary>Product, cart and receipt.</summary>
public sealed class BuyingTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    private static async Task<ProductPresenter> OpenProduct(Shop shop, string name)
    {
        var products = shop.PresenterOf<HomePresenter>(Places.Home).Scope.Products!;
        await shop.Do(() => products.OnOpenProduct(products.Products.Single(p => p.Name == name).Id));
        return shop.PresenterOf<ProductPresenter>(Places.Product);
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
        Assert.Same(product.Scope, shop.PresenterOf<HomePresenter>(Places.Home).Scope.Content);
    }

    [Fact]
    public async Task Product_ThatDoesNotExist_IsRefused_FromTheListAndFromTheAddress()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        var home = shop.PresenterOf<HomePresenter>(Places.Home);

        await shop.Do(() => home.Scope.Products!.OnOpenProduct(999_999));
        Assert.Equal(HomePresenter.ProductNotFound, shop.App.Scope.ErrorMessage);
        Assert.Equal("home", shop.History.Token);

        var deep = await Shop.Start(db, "home/product?pr-id=999999", shop.Session);
        Assert.Null(deep.Maybe<ProductPresenter>(Places.Product));
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
        var cart = shop.PresenterOf<CartPresenter>(Places.Cart);
        Assert.Equal(2, cart.Scope.Lines.Single().Quantity);
        Assert.Equal("R$ 5,34", cart.Scope.TotalLabel);
        Assert.Equal("home/cart", shop.History.Token);

        product = await OpenProduct(shop, "Fita veda rosca");
        await shop.Do(() => product.Scope.OnAddToCart(1)); // the same product again: its quantity grows
        Assert.Equal(3, shop.PresenterOf<HomePresenter>(Places.Home).Scope.CartCount); // the badge follows (Kotlin bug fixed)
    }

    [Fact]
    public async Task Cart_QuantityBelowOneIsRefused_AndTheMessageGoesAway()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        await shop.Do(() => OpenProduct(shop, "Bola de vôlei").ContinueWith(t => t.Result.Scope.OnAddToCart(1)).Unwrap());
        var cart = shop.PresenterOf<CartPresenter>(Places.Cart);
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

        await shop.Do(shop.PresenterOf<CartPresenter>(Places.Cart).Scope.Lines.Single().OnRemove);

        Assert.Equal("home", shop.History.Token);
        Assert.Equal(0, shop.PresenterOf<HomePresenter>(Places.Home).Scope.CartCount);
    }

    [Fact]
    public async Task Buying_RecordsThePurchase_ShowsTheReceipt_AndRefreshesHome()
    {
        var shop = await Shop.LoggedIn(db, "beotrano");
        var product = await OpenProduct(shop, "Cafeteira design italiano");
        await shop.Do(() => product.Scope.OnAddToCart(2));

        await shop.Do(shop.PresenterOf<CartPresenter>(Places.Cart).Scope.OnBuy);

        var receipt = shop.PresenterOf<ReceiptPresenter>(Places.Receipt);
        Assert.True(receipt.Scope.NotifySuccess);
        Assert.Equal([("Cafeteira design italiano", 2, "R$ 199,99")], receipt.Scope.Lines.Select(l => (l.Description, l.Quantity, l.ValueLabel)));
        Assert.Equal("R$ 399,98", receipt.Scope.TotalLabel);
        Assert.Equal("18/11/2026", receipt.Scope.DateLabel); // the application's clock
        Assert.StartsWith("home/receipt?rc-id=", shop.History.Token, StringComparison.Ordinal);

        var home = shop.PresenterOf<HomePresenter>(Places.Home);
        Assert.Equal(0, home.Scope.CartCount);
        Assert.Equal("R$ 399,98", home.Scope.Purchases!.Purchases[0].TotalLabel);

        // The banner belongs to the trip from the cart: the same receipt opened by its address does not show it.
        var reopened = await Shop.Start(db, shop.History.Token, shop.Session);
        Assert.False(reopened.PresenterOf<ReceiptPresenter>(Places.Receipt).Scope.NotifySuccess);
    }

    [Fact]
    public async Task Receipt_OfSomeoneElse_IsTheSameAsOneThatDoesNotExist()
    {
        var admin = await Shop.LoggedIn(db, "admin");
        var adminPurchase = admin.PresenterOf<HomePresenter>(Places.Home).Scope.Purchases!.Purchases[0].Id;
        var shop = await Shop.LoggedIn(db, "fulano");

        await shop.Do(() => shop.PresenterOf<HomePresenter>(Places.Home).Scope.Purchases!.OnOpenReceipt(adminPurchase));

        Assert.Equal(HomePresenter.ReceiptNotFound, shop.App.Scope.ErrorMessage);
        Assert.Null(shop.Maybe<ReceiptPresenter>(Places.Receipt));

        var deep = await Shop.Start(db, $"home/receipt?rc-id={adminPurchase}", shop.Session);
        Assert.Null(deep.Maybe<ReceiptPresenter>(Places.Receipt));
        Assert.Equal(HomePresenter.ReceiptNotFound, deep.App.Scope.ErrorMessage); // a message for the user, not the log's
    }

    [Fact]
    public async Task ErrorToast_GoesAwayOnItsOwn()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        await shop.Do(() => shop.PresenterOf<HomePresenter>(Places.Home).Scope.Products!.OnOpenProduct(999_998));
        Assert.NotNull(shop.App.Scope.ErrorMessage);

        shop.Time.Advance(Modules.Main.MainPresenter.ErrorToastDuration);
        await shop.Settle();

        Assert.Null(shop.App.Scope.ErrorMessage);
    }

    [Fact]
    public void KeyPrefixes_AreUniqueAcrossTheApplication()
    {
        var prefixes = typeof(Places).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Keys", StringComparison.Ordinal))
            .Select(t => t.GetField("Prefix", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                                              | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(f => f is not null)
            .Select(f => (string)f!.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(prefixes.Count, prefixes.Distinct().Count());
        Assert.Equal(["hm", "mn", "pr", "rc"], prefixes.Order());
    }
}

/// <summary>The images on PostgreSQL.</summary>
public sealed class ImageTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    [Fact]
    public async Task ProductImages_AreStoredAndRead_AndAMissingProductSaysSo()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var products = await db.Store!.Repositories.Products.Fetch(new Domain.Products.ProductCriteria(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, products.Count);
        foreach (var product in products)
        {
            var image = await db.Store.Repositories.Products.FetchImage(product.Id!.Value, TestContext.Current.CancellationToken);
            Assert.True(image is { Length: > 1000 } && image[1] == (byte)'P' && image[2] == (byte)'N' && image[3] == (byte)'G');
        }

        await Assert.ThrowsAsync<KeyNotFoundException>(() => db.Store.Repositories.Products.FetchImage(999_999, TestContext.Current.CancellationToken));
    }
}
