using Shopping.I18n;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home.Admin;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Products;
using Shopping.Presentation.Modules.Home.Purchases;
using Shopping.Presentation.Modules.Home.Receipt;
using Shopping.Presentation.Modules.Login;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home;

/// <summary>
/// The restricted area: the session guard, the shell (greeting, cart badge, exit) and, when nothing is open below
/// it, the products and purchases panels. It owns the cart and the two panel presenters.
/// </summary>
public sealed class HomePresenter : CubePresenter<RootPresenter, HomeScope>
{
    public static Message ProductNotFound => Texts.ErrorProductNotFound();

    public static Message ReceiptNotFound => Texts.ErrorReceiptNotFound();

    private ProductsPanelPresenter? _products;
    private PurchasesPanelPresenter? _purchases;

    public HomePresenter(RootPresenter app)
        : base(app, new HomeScope())
    {
        Cart = new CartManager(app.Repositories, app.Time);
        Cart.Changed += OnCartChanged;
        Cart.Committed += OnCartCommitted;
    }

    public CartManager Cart { get; }

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        // Guard: nothing here without a session. Redirect with the intent in transit.
        if (App.Subject is not { } subject)
        {
            await App.FlipToIntent(intent.Redirect(LoginKeys.Place));
            return false;
        }

        var keys = new HomeKeys(App, intent);
        keys.ParentSlot(Scope); // the shell appears before any wait

        if (initialization)
        {
            Scope.Greeting = Texts.HomeGreeting(name: subject.NickName);
            Scope.OnOpenCart = Action(OnOpenCart);
            Scope.OnExit = Action(OnExit);
            Scope.OnHome = Action(OnHome);
            Scope.ShowAdmin = subject.Can(Admin.AdminPresenter.Permission);
            Scope.OnOpenAdmin = Action(OnOpenAdmin);

            _products = new ProductsPanelPresenter(App, OpenProduct);
            _purchases = new PurchasesPanelPresenter(App, OpenReceipt);
            Scope.Products = _products.Scope;
            Scope.Purchases = _purchases.Scope;
            _ = _products.Load(); // in parallel: the flip does not wait for the catalogue
        }

        _ = _purchases!.ShowPage(keys.Page); // reloads only when the page changed

        if (last)
        {
            Scope.Content = null; // the panels
        }
        else
        {
            keys.ParentSlot = content => Scope.Content = content;
            keys.Home = this;
        }

        return true;
    }

    public override void PublishParameters(FlipIntent intent)
    {
        if (_purchases is not null)
        {
            new HomeKeys(App, intent).Page = _purchases.Page;
        }
    }

    public override void Release()
    {
        Cart.Changed -= OnCartChanged;
        Cart.Committed -= OnCartCommitted;
        _products?.Release();
        _purchases?.Release();
        base.Release();
    }

    protected override void OnBeforeScopeUpdate() => Scope.CartCount = Cart.ItemCount;

    private void OnCartChanged() => UpdateManager.Update(); // the badge is derived in OnBeforeScopeUpdate

    private void OnCartCommitted(long purchaseId)
    {
        _ = _products?.Load(); // stock, prices
        _ = _purchases?.ShowPage(1, reload: true);
    }

    private async Task OpenProduct(long productId)
    {
        try
        {
            await new ProductKeys(App) { ProductId = productId }.Flip();
        }
        catch (ProductNotFoundException)
        {
            App.ShowError(ProductNotFound);
        }
    }

    private async Task OpenReceipt(long purchaseId)
    {
        try
        {
            await new ReceiptKeys(App) { PurchaseId = purchaseId }.Flip();
        }
        catch (PurchaseNotFoundException)
        {
            App.ShowError(ReceiptNotFound);
        }
    }

    private async Task OnExit()
    {
        Cart.Clear();
        await App.SignOut();
        await new LoginKeys(App).Flip();
    }

    private Task OnOpenCart() => new CartKeys(App).Flip();

    private Task OnHome() => new HomeKeys(App).Flip();

    private Task OnOpenAdmin() => new AdminKeys(App).Flip();
}
