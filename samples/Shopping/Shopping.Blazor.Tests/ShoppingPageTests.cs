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
            _ctx.Services.AddSingleton(db.Store.Repositories);
            _ctx.Services.AddSingleton<IAuthentication>(new LocalAuthentication(db.Store.Repositories,
                id => Domain.Users.HistoryKeys.Derive(db.HistoryMaster, id)));
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
        Assert.StartsWith("home/product?_e=", Address, StringComparison.Ordinal); // sealed with the user's key (D-22)
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
    public void Exit_GoesBackToLogin()
    {
        var page = LogIn(Open(), "beotrano", "beotrano");
        page.WaitForElement(".exit", Wait).Click();

        page.WaitForElement(".login-card", Wait);
        Assert.Equal("login", Address);
    }
}
