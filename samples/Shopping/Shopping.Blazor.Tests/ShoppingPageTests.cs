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

    private static TimeSpan Wait => TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static IRenderedComponent<Shell> LogIn(IRenderedComponent<Shell> page, string user, string password)
    {
        page.WaitForElement(".login-card", Wait);
        page.Find(".login-card .user").Input(user);
        page.Find(".login-card .password").Input(password);
        page.Find(".login-card").Submit();
        return page;
    }

    [Fact]
    public void Login_WrongPasswordShowsTheMessage_RightOneOpensHome()
    {
        var page = LogIn(Open(), "admin", "wrong");
        page.WaitForAssertion(() => Assert.Equal("Usuário ou senha não reconhecido!", page.Find(".login-card .error").TextContent), Wait);

        LogIn(page, "admin", "admin");
        page.WaitForAssertion(() => Assert.Equal("Olá, João da Silva", page.Find(".greeting").TextContent), Wait);
        Assert.Equal("home", Address);
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".product-card").Count), Wait);
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".purchase-card").Count), Wait);
    }

    [Fact]
    public void Buying_FromTheCatalogue_ToTheReceipt()
    {
        var page = LogIn(Open(), "fulano", "fulano");
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".product-card").Count), Wait);

        page.FindAll(".product-card").Single(c => c.QuerySelector(".name")!.TextContent == "Bola de vôlei").Click();
        page.WaitForAssertion(() => Assert.Equal("Bola de vôlei", page.Find(".product-page h2").TextContent), Wait);
        Assert.StartsWith("home/product?_e=", Address, StringComparison.Ordinal); // sealed with the user's key
        Assert.Equal(3, page.FindAll(".description li").Count);

        page.Find(".product-page .plus").Click();
        page.Find(".product-page .plus").Click();
        page.WaitForAssertion(() => Assert.Equal("3", page.Find(".product-page .quantity .value").TextContent), Wait);
        page.Find(".product-page .add").Click();

        page.WaitForAssertion(() => Assert.Equal("3", page.Find(".cart-line .value").TextContent), Wait);
        Assert.Equal("home/cart", Address);
        Assert.Equal("3", page.Find(".badge").TextContent);
        Assert.Contains("R$ 135,90", page.Find(".cart-page .total").TextContent, StringComparison.Ordinal);

        page.Find(".cart-page .buy").Click();

        page.WaitForAssertion(() => Assert.Equal("Compra realizada com sucesso!", page.Find(".receipt-page .success").TextContent), Wait);
        Assert.StartsWith("home/receipt?_e=", Address, StringComparison.Ordinal);
        Assert.Contains("R$ 135,90", page.Find(".receipt-page .total").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".badge"));
    }

    [Fact]
    public async Task Admin_CorrectsAPurchase_AndTheBuyerSeesItOnTheReceipt()
    {
        Assert.SkipWhen(_db.Store is null, _db.SkipReason);
        var store = _db.Store!.Repositories;
        var fulano = (await store.Users.Fetch(new Domain.Users.UserCriteria().UserName.Eq("fulano"), limit: 1,
            cancellationToken: Ct))[0];
        var ball = (await store.Products.Fetch(new Domain.Products.ProductCriteria().Name.Eq("Bola de vôlei"), limit: 1,
            cancellationToken: Ct))[0];
        var tape = (await store.Products.Fetch(new Domain.Products.ProductCriteria().Name.Eq("Fita veda rosca"), limit: 1,
            cancellationToken: Ct))[0];
        var purchase = new Domain.Purchases.Purchase
        {
            User = new Domain.Users.User { Id = fulano.Id },
            BuyDate = DateTimeOffset.UtcNow,
            Items =
            [
                new Domain.Purchases.PurchaseItem { Product = new Domain.Products.Product { Id = ball.Id }, Amount = 2, Price = 45.30m },
                new Domain.Purchases.PurchaseItem { Product = new Domain.Products.Product { Id = tape.Id }, Amount = 1, Price = 2.67m },
            ],
        };
        await store.Purchases.Insert(purchase, cancellationToken: Ct);

        var page = LogIn(Open(), "admin", "admin");
        page.WaitForElement(".admin-button", Wait).Click();
        page.WaitForElement(".admin-page", Wait);
        page.Find(".admin-filter .customer").Input("Fulano");
        page.Find(".admin-filter").Submit();
        page.WaitForAssertion(() => Assert.All(page.FindAll(".admin-row .customer"), c => Assert.Equal("Fulano de Tal", c.TextContent)), Wait);
        page.FindAll(".admin-row").Single(r => r.QuerySelector(".number")!.TextContent == $"Nº {purchase.Id}").Click();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".amend-line").Count), Wait);
        Assert.True(page.Find(".amend-page .confirm").HasAttribute("disabled"));
        page.FindAll(".amend-line")[0].QuerySelector(".minus")!.Click();
        page.WaitForAssertion(() => Assert.Equal("1", page.FindAll(".amend-line")[0].QuerySelector(".new")!.TextContent), Wait);
        page.Find(".amend-page textarea").Input("Uma bola não foi entregue");
        page.WaitForAssertion(() => Assert.False(page.Find(".amend-page .confirm").HasAttribute("disabled")), Wait);
        page.Find(".amend-page .confirm").Click();

        page.WaitForAssertion(() => Assert.Equal("Correção registrada.", page.Find(".amend-page .success").TextContent), Wait);
        Assert.Contains("Uma bola não foi entregue", page.Find(".amend-page .history").TextContent, StringComparison.Ordinal);
        Assert.Contains("Bola de vôlei: 2 → 1", page.Find(".amend-page .history").TextContent, StringComparison.Ordinal);

        page.Find(".amend-page .back").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".admin-row .corrected")), Wait);
        Assert.Equal("Fulano", page.Find(".admin-filter .customer").GetAttribute("value"));

        page.Find(".exit").Click();
        LogIn(page, "fulano", "fulano");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".purchase-card")), Wait);
        Assert.Empty(page.FindAll(".admin-button"));
        page.FindAll(".purchase-card")[0].Click();
        page.WaitForAssertion(() => Assert.StartsWith("Compra corrigida em ", page.Find(".receipt-page .correction").TextContent, StringComparison.Ordinal), Wait);
        Assert.Contains("R$ 47,97", page.Find(".receipt-page .total").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Exit_GoesBackToLogin()
    {
        var page = LogIn(Open(), "beotrano", "beotrano");
        page.WaitForElement(".exit", Wait).Click();

        page.WaitForElement(".login-card", Wait);
        Assert.Equal("login", Address);
    }
}
