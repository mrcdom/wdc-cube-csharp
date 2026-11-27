using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Modules.Home.ProductDetail.Calls;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.ProductDetail;

/// <summary>A product, under Home. A product that does not exist refuses the navigation.</summary>
public sealed class ProductPresenter(RootPresenter app) : CubePresenter<RootPresenter, ProductScope>(app, new ProductScope())
{
    public const string QuantityBelowOne = "The quantity of items in the cart must be at least 1 (one).";

    private long? _productId;
    private decimal _price;
    private HomePresenter? _home;

    public override async Task<bool> ApplyParameters(FlipIntent intent, bool initialization, bool last)
    {
        var keys = new ProductKeys(App, intent);
        _home = keys.Home;

        if (keys.ProductId is not { } productId)
        {
            await App.FlipToIntent(intent.Redirect(HomeKeys.Place)); // nothing to show: back to the panels
            return false;
        }

        if (initialization)
        {
            Scope.OnAddToCart = Action<int>(OnAddToCart);
            Scope.OnBack = Action(OnBack);
        }

        if (productId != _productId)
        {
            await Load(productId); // a light query: whether the product exists decides the navigation
        }

        keys.ParentSlot(Scope);
        return true;
    }

    public override void PublishParameters(FlipIntent intent) => new ProductKeys(App, intent).ProductId = _productId;

    private async Task Load(long productId)
    {
        var product = await new ProductQuery(App.Repositories, productId).Run();

        _productId = productId;
        _price = product.Price ?? 0;
        Scope.Id = productId;
        Scope.Name = product.Name ?? string.Empty;
        Scope.DescriptionItems = Formats.DescriptionItems(product.Description);
        Scope.PriceLabel = Formats.Money(_price);
        Scope.ImageUrl = Formats.ProductImageUrl(productId);
        Scope.ErrorMessage = null;
    }

    private async Task OnAddToCart(int quantity)
    {
        if (quantity < 1)
        {
            Scope.ErrorMessage = QuantityBelowOne;
            return;
        }

        var cart = _home?.Cart ?? throw new InvalidOperationException("The product is shown outside Home.");
        cart.Add(_productId!.Value, Scope.Name, _price, quantity);
        await new CartKeys(App).Flip();
    }

    private Task OnBack() => new HomeKeys(App).Flip();
}
