using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Blazor.Views;
using Shopping.Blazor.Views.Pages;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Tests;
using Wdc.Cube.Blazor;

namespace Shopping.Blazor.Tests;

/// <summary>
/// The whole Shopping page on Blazor's renderer, with the real application, views and database (throwaway schema).
/// </summary>
public sealed class ShoppingPageTests : IClassFixture<ShoppingDatabaseFixture>, IDisposable
{
    private readonly ShoppingDatabaseFixture _db;
    private readonly BunitContext _ctx = new();

    public ShoppingPageTests(ShoppingDatabaseFixture db)
    {
        _db = db;
        if (db.Store is not null)
        {
            var (repositories, authentication) = db.Connect(); // protected, as in the Blazor Server host
            _ctx.Services.AddSingleton(repositories);
            _ctx.Services.AddSingleton(authentication);
        }

        _ctx.Services.AddSingleton<ISessionStore>(new MemorySessionStore());
        _ctx.Services.AddSingleton(TimeProvider.System);
        _ctx.Services.AddCubeViews(ViewCatalog.Register);
    }

    private NavigationManager Navigation => _ctx.Services.GetRequiredService<NavigationManager>();

    private string Address => Navigation.ToBaseRelativePath(Navigation.Uri);

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<Shell> Open(string address = "")
    {
        Assert.SkipWhen(_db.Store is null, _db.SkipReason);
        if (address.Length > 0)
        {
            Navigation.NavigateTo(address);
        }

        return _ctx.Render<Shell>();
    }

    // Finds and acts on the renderer's thread: a render in between would replace the handlers of the element found.
    private static void On(IRenderedComponent<Shell> page, Action act) => page.InvokeAsync(act).GetAwaiter().GetResult();

    private static TimeSpan Wait => TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static IRenderedComponent<Shell> LogIn(IRenderedComponent<Shell> page, string user, string password)
    {
        page.WaitForElement(".login-card", Wait);
        On(page, () => page.Find(".login-card .user").Input(user));
        On(page, () => page.Find(".login-card .password").Input(password));
        On(page, () => page.Find(".login-card").Submit());
        return page;
    }

    [Fact]
    public void Login_WrongPasswordShowsTheMessage_RightOneOpensHome()
    {
        var page = LogIn(Open(), "admin", "wrong");
        page.WaitForAssertion(() => Assert.Equal("Username or password not recognized!", page.Find(".login-card .error").TextContent), Wait);

        LogIn(page, "admin", "admin");
        page.WaitForAssertion(() => Assert.Equal("Hello, John Smith", page.Find(".greeting").TextContent), Wait);
        Assert.Equal("home", Address);
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".product-card").Count), Wait);
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".purchase-card").Count), Wait);
    }

    [Fact]
    public void Buying_FromTheCatalogue_ToTheReceipt()
    {
        var page = LogIn(Open(), "fulano", "fulano");
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".product-card").Count), Wait);

        On(page, () => page.FindAll(".product-card").Single(c => c.QuerySelector(".name")!.TextContent == "Volleyball").Click());
        page.WaitForAssertion(() => Assert.Equal("Volleyball", page.Find(".product-page h2").TextContent), Wait);
        Assert.StartsWith("home/product?_e=", Address, StringComparison.Ordinal); // sealed with the user's key
        Assert.Equal(3, page.FindAll(".description li").Count);

        // Each click redraws the page from the action, off the test's thread: wait for it before finding the button again.
        On(page, () => page.Find(".product-page .plus").Click());
        page.WaitForAssertion(() => Assert.Equal("2", page.Find(".product-page .quantity .value").TextContent), Wait);
        On(page, () => page.Find(".product-page .plus").Click());
        page.WaitForAssertion(() => Assert.Equal("3", page.Find(".product-page .quantity .value").TextContent), Wait);
        On(page, () => page.Find(".product-page .add").Click());

        page.WaitForAssertion(() => Assert.Equal("3", page.Find(".cart-line .value").TextContent), Wait);
        Assert.Equal("home/cart", Address);
        Assert.Equal("3", page.Find(".badge").TextContent);
        Assert.Contains("$135.90", page.Find(".cart-page .total").TextContent, StringComparison.Ordinal);

        On(page, () => page.Find(".cart-page .buy").Click());

        page.WaitForAssertion(() => Assert.Equal("Purchase completed successfully!", page.Find(".receipt-page .success").TextContent), Wait);
        Assert.StartsWith("home/receipt?_e=", Address, StringComparison.Ordinal);
        Assert.Contains("$135.90", page.Find(".receipt-page .total").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".badge"));
    }

    [Fact]
    public async Task Admin_CorrectsAPurchase_AndTheBuyerSeesItOnTheReceipt()
    {
        Assert.SkipWhen(_db.Store is null, _db.SkipReason);
        var store = _db.Store!.Repositories;
        var fulano = (await store.Users.Fetch(new Domain.Users.UserCriteria().UserName.Eq("fulano"), limit: 1,
            cancellationToken: Ct))[0];
        var ball = (await store.Products.Fetch(new Domain.Products.ProductCriteria().Name.Eq("Volleyball"), limit: 1,
            cancellationToken: Ct))[0];
        var tape = (await store.Products.Fetch(new Domain.Products.ProductCriteria().Name.Eq("Thread Seal Tape"), limit: 1,
            cancellationToken: Ct))[0];
        var purchase = new Domain.Purchases.Purchase
        {
            User = new Domain.Users.User { Id = fulano.Id },
            // Ahead of the purchases other test runs make as fulano meanwhile, on the same database: it stays first on
            // the administrator's first page and in fulano's purchases.
            BuyDate = DateTimeOffset.UtcNow.AddYears(1),
            Items =
            [
                new Domain.Purchases.PurchaseItem { Product = new Domain.Products.Product { Id = ball.Id }, Amount = 2, Price = 45.30m },
                new Domain.Purchases.PurchaseItem { Product = new Domain.Products.Product { Id = tape.Id }, Amount = 1, Price = 2.67m },
            ],
        };
        await store.Purchases.Insert(purchase, cancellationToken: Ct);

        var page = LogIn(Open(), "admin", "admin");
        page.WaitForElement(".admin-button", Wait);
        On(page, () => page.Find(".admin-button").Click());
        page.WaitForElement(".admin-page", Wait);
        On(page, () => page.Find(".admin-filter .customer").Input("Doe"));
        On(page, () => page.Find(".admin-filter").Submit());
        // The list from before the filter can already pass the checks below, and a click on it would land on handlers
        // the filtered list replaces: wait until the filtered list is the one shown.
        page.WaitForAssertion(() =>
        {
            var admin = page.FindComponent<AdminView>().Instance.Scope;
            Assert.Equal("Doe", admin.CustomerFilter);
            Assert.False(admin.Loading);
        }, Wait);
        page.WaitForAssertion(() => Assert.All(page.FindAll(".admin-row .customer"), c => Assert.Equal("John Doe", c.TextContent)), Wait);
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".admin-row"), r => r.QuerySelector(".number")!.TextContent == $"#{purchase.Id}"), Wait);
        On(page, () => page.FindAll(".admin-row").Single(r => r.QuerySelector(".number")!.TextContent == $"#{purchase.Id}").Click());

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".amend-line").Count), Wait);
        Assert.True(page.Find(".amend-page .confirm").HasAttribute("disabled"));
        On(page, () => page.FindAll(".amend-line")[0].QuerySelector(".minus")!.Click());
        page.WaitForAssertion(() => Assert.Equal("1", page.FindAll(".amend-line")[0].QuerySelector(".new")!.TextContent), Wait);
        On(page, () => page.Find(".amend-page textarea").Input("One ball was not delivered"));
        page.WaitForAssertion(() => Assert.False(page.Find(".amend-page .confirm").HasAttribute("disabled")), Wait);
        On(page, () => page.Find(".amend-page .confirm").Click());

        page.WaitForAssertion(() => Assert.Equal("Correction recorded.", page.Find(".amend-page .success").TextContent), Wait);
        Assert.Contains("One ball was not delivered", page.Find(".amend-page .history").TextContent, StringComparison.Ordinal);
        Assert.Contains("Volleyball: 2 → 1", page.Find(".amend-page .history").TextContent, StringComparison.Ordinal);

        On(page, () => page.Find(".amend-page .back").Click());
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".admin-row .corrected")), Wait);
        Assert.Equal("Doe", page.Find(".admin-filter .customer").GetAttribute("value"));

        On(page, () => page.Find(".exit").Click());
        LogIn(page, "fulano", "fulano");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".purchase-card")), Wait);
        Assert.Empty(page.FindAll(".admin-button"));
        On(page, () => page.FindAll(".purchase-card")[0].Click());
        page.WaitForAssertion(() => Assert.StartsWith("Purchase corrected on ", page.Find(".receipt-page .correction").TextContent, StringComparison.Ordinal), Wait);
        Assert.Contains("$47.97", page.Find(".receipt-page .total").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void InPortuguese_TheFixedTextsAndTheMessagesOfThePresentation()
    {
        var english = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR"); // as Accept-Language sets it per circuit
        try
        {
            var page = Open();
            page.WaitForElement(".login-card", Wait);
            Assert.Equal("Entre para continuar", page.Find(".login-card .subtitle").TextContent);
            Assert.Equal("Entrar", page.Find(".login-card .enter").TextContent.Trim());

            LogIn(page, "fulano", "errada");
            page.WaitForAssertion(() => Assert.Equal("Usuário ou senha não reconhecidos!", page.Find(".login-card .error").TextContent), Wait);

            LogIn(page, "fulano", "fulano");
            page.WaitForAssertion(() => Assert.Equal("Olá, John Doe", page.Find(".greeting").TextContent), Wait);
            Assert.Equal("Sair", page.Find(".exit").TextContent);
            page.WaitForAssertion(() => Assert.StartsWith("Produtos", page.Find(".products-panel h2").TextContent, StringComparison.Ordinal), Wait);
            page.WaitForAssertion(() => Assert.EndsWith(" itens", page.Find(".products-panel .chip").TextContent, StringComparison.Ordinal), Wait);
            Assert.Contains("Anterior", page.Find(".purchases-panel .previous").TextContent, StringComparison.Ordinal);

            On(page, () => page.Find(".cart-button").Click());
            page.WaitForAssertion(() => Assert.Equal("Carrinho de compras", page.Find(".cart-page h2").TextContent), Wait);
            Assert.Equal("Seu carrinho está vazio", page.Find(".cart-page .empty p:not(.big):not(.muted)").TextContent);
            Assert.Equal("Continuar comprando", page.Find(".cart-page .continue").TextContent);
        }
        finally
        {
            CultureInfo.CurrentUICulture = english;
        }
    }

    [Fact]
    public void Exit_GoesBackToLogin()
    {
        var page = LogIn(Open(), "beotrano", "beotrano");
        page.WaitForElement(".exit", Wait);
        On(page, () => page.Find(".exit").Click());

        page.WaitForElement(".login-card", Wait);
        Assert.Equal("login", Address);
    }
}
