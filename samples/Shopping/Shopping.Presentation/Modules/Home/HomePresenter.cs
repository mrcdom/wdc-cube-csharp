using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Shopping.Presentation.Modules.Home.Products;
using Shopping.Presentation.Modules.Home.Purchases;
using Shopping.Presentation.Modules.Home.Receipt;
using Shopping.Presentation.Modules.Main;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home;

/// <summary>
/// The restricted area: the session guard, the shell (greeting, cart badge, exit) and, when nothing is open below
/// it, the products and purchases panels. It owns the cart and the two panel presenters.
/// </summary>
public sealed class HomePresenter : CubePresenter<MainPresenter, HomeScope>
{
    public const string ProductNotFound = "Código do produto não localizado.";
    public const string ReceiptNotFound = "Código do recibo não localizado.";

    private ProductsPanelPresenter? _products;
    private PurchasesPanelPresenter? _purchases;

    public HomePresenter(MainPresenter app)
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
            await App.FlipToIntent(intent.Redirect(Places.Login));
            return false;
        }

        var keys = new HomeKeys(App, intent);
        keys.ParentSlot(Scope); // the shell appears before any wait

        if (initialization)
        {
            Scope.Greeting = $"Olá, {subject.NickName}";
            Scope.OnOpenCart = Action(() => Flip(Places.Cart));
            Scope.OnExit = Action(OnExit);
            Scope.OnHome = Action(() => Flip(Places.Home));
            Scope.ShowAdmin = subject.Can(Admin.AdminPresenter.Permission);
            Scope.OnOpenAdmin = Action(() => Flip(Places.Admin));

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
        await Flip(Places.Login);
    }
}
