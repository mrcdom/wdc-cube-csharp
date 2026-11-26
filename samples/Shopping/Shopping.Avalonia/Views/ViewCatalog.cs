using Shopping.Presentation.Modules;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Admin;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Products;
using Shopping.Presentation.Modules.Home.Purchases;
using Shopping.Presentation.Modules.Home.Receipt;
using Shopping.Presentation.Modules.Login;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

/// <summary>Which control draws each scope (the same list as the Blazor catalog, plus the panels and cart lines).</summary>
public static class ViewCatalog
{
    public static ScopeViewLocator Create() => new ScopeViewLocator()
        .Register<RootScope>(() => new RootView())
        .Register<LoginScope>(() => new LoginView())
        .Register<HomeScope>(() => new HomeView())
        .Register<ProductsPanelScope>(() => new ProductsPanelView())
        .Register<PurchasesPanelScope>(() => new PurchasesPanelView())
        .Register<ProductScope>(() => new ProductView())
        .Register<CartScope>(() => new CartView())
        .Register<CartLineScope>(() => new CartLineView())
        .Register<ReceiptScope>(() => new ReceiptView())
        .Register<AdminScope>(() => new AdminView())
        .Register<AmendScope>(() => new AmendView())
        .Register<AmendLineScope>(() => new AmendLineView());
}
