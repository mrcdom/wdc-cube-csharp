using Shopping.Presentation.Modules.Home;
using Shopping.Presentation.Modules.Home.Cart;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Main;
using Wdc.Cube;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.ProductDetail;

/// <summary>A product, under Home. A product that does not exist refuses the navigation.</summary>
public sealed class ProductPresenter(MainPresenter app) : CubePresenter<MainPresenter, ProductScope>(app, new ProductScope())
{
    public const string QuantityBelowOne = "A quantidade de itens no carrinho deve ser maior ou igual a 1 (um).";

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
            Scope.OnBack = Action(() => new HomeKeys(App).Flip());
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
        var product = await App.Repositories.Products.FetchById(productId, new Domain.Products.Product
        {
            Id = Mark.Int64, Name = Mark.String, Price = Mark.Decimal, Description = Mark.String,
        }) ?? throw new ProductNotFoundException(productId);

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
}
