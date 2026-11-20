using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Admin;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Receipt;
using Shopping.Presentation.Modules.Login;
using Shopping.Presentation.Modules.Main;
using Wdc.Cube;

namespace Shopping.Presentation;

/// <summary>The cube of the Shopping demo. The folder tree mirrors it.</summary>
public static class Places
{
    /// <summary>The application itself; going there means going home.</summary>
    public static readonly Place Main = Place.Root;

    /// <summary>Public: outside the guard.</summary>
    public static readonly Place Login = Place.Create<MainPresenter>("login", app => new LoginPresenter(app));

    /// <summary>The session guard and the shell (header, cart badge) of everything below it.</summary>
    public static readonly Place Home = Place.Create<MainPresenter>("home", app => new HomePresenter(app));

    public static readonly Place Product = Home.Child<MainPresenter>("product", app => new ProductPresenter(app));

    public static readonly Place Cart = Home.Child<MainPresenter>("cart", app => new CartPresenter(app));

    public static readonly Place Receipt = Home.Child<MainPresenter>("receipt", app => new ReceiptPresenter(app));

    /// <summary>The purchases of every customer (decision D-25): only for whoever may correct them.</summary>
    public static readonly Place Admin = Home.Child<MainPresenter>("admin", app => new AdminPresenter(app));

    /// <summary>Correcting one purchase, below the list it came from.</summary>
    public static readonly Place Amend = Admin.Child<MainPresenter>("amend", app => new AmendPresenter(app));

    public static IReadOnlyList<Place> All { get; } = [Login, Home, Product, Cart, Receipt, Admin, Amend];
}
