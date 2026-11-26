using Shopping.Presentation.Modules;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Admin;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Receipt;
using Shopping.Presentation.Modules.Login;
using Wdc.Cube.Blazor;

namespace Shopping.Blazor.Views;

/// <summary>Which component draws each scope that can land in a slot.</summary>
public static class ViewCatalog
{
    public static void Register(ViewRegistry views)
    {
        ArgumentNullException.ThrowIfNull(views);
        views
            .Register<RootScope, RootView>()
            .Register<LoginScope, LoginView>()
            .Register<HomeScope, HomeView>()
            .Register<ProductScope, ProductView>()
            .Register<CartScope, CartView>()
            .Register<ReceiptScope, ReceiptView>()
            .Register<AdminScope, AdminView>()
            .Register<AmendScope, AmendView>();
    }
}
