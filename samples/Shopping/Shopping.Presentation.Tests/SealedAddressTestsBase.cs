using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.ProductDetail;

namespace Shopping.Presentation.Tests;

/// <summary>Sealed addresses with each user's key.</summary>
public abstract class SealedAddressTestsBase(ShoppingDatabaseFixture db)
{
    private static async Task<long> FirstProduct(Shop shop)
    {
        var products = shop.PresenterOf<HomePresenter>(Places.Home).Scope.Products!;
        return products.Products[0].Id;
    }

    [Fact]
    public async Task AfterLogin_TheAddressIsSealed_AndAReloadOpensIt()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        var productId = await FirstProduct(shop);

        await shop.Do(() => shop.PresenterOf<HomePresenter>(Places.Home).Scope.Products!.OnOpenProduct(productId));

        Assert.Equal($"home/product?pr-id={productId}", shop.History.Token); // the state...
        Assert.StartsWith("home/product?_e=", shop.History.EncodedToken, StringComparison.Ordinal); // ...the address
        Assert.DoesNotContain("pr-id", shop.History.EncodedToken, StringComparison.Ordinal);

        var reloaded = await Shop.Start(db, shop.History.EncodedToken, shop.Session); // F5 on the sealed address
        Assert.Equal(productId, reloaded.PresenterOf<ProductPresenter>(Places.Product).Scope.Id);
    }

    [Fact]
    public async Task AnotherUsersSealedLink_DoesNotOpen()
    {
        var fulano = await Shop.LoggedIn(db, "fulano");
        var productId = await FirstProduct(fulano);
        await fulano.Do(() => fulano.PresenterOf<HomePresenter>(Places.Home).Scope.Products!.OnOpenProduct(productId));
        var sealedLink = fulano.History.EncodedToken;

        var beotrano = await Shop.LoggedIn(db, "beotrano");
        var opened = await Shop.Start(db, sealedLink, beotrano.Session);

        Assert.NotEqual(productId, opened.Maybe<ProductPresenter>(Places.Product)?.Scope.Id); // the parameters are dropped
        Assert.Equal("Olá, Beotrano de Alguma Coisa", opened.PresenterOf<HomePresenter>(Places.Home).Scope.Greeting);
    }

    [Fact]
    public async Task Logout_StopsSealing()
    {
        var shop = await Shop.LoggedIn(db, "fulano");
        Assert.NotNull(shop.History.Codec);

        await shop.Do(shop.PresenterOf<HomePresenter>(Places.Home).Scope.OnExit);

        Assert.Null(shop.History.Codec);
        Assert.Equal("login", shop.History.EncodedToken);
    }
}
