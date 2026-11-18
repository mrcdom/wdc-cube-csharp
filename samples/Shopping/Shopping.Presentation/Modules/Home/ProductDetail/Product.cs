using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Main;
using Wdc.Cube;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.ProductDetail;

public sealed class ProductKeys : HomeKeys
{
    public new const string Prefix = "pr";

    private static class P
    {
        /// <summary>The product shown.</summary>
        public const string ProductId = $"{Prefix}-id";
    }

    public ProductKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Places.Product)
    {
    }

    /// <summary>A positive id, or <see langword="null"/> when the address carries none it can use.</summary>
    public long? ProductId
    {
        get => Intent.GetInt64(P.ProductId) is > 0 and var id ? id : null;
        set => Intent.SetParameter(P.ProductId, value);
    }
}

public sealed class ProductScope : Scope
{
    public long Id { get; set => Set(ref field, value); }

    public string Name { get; set => Set(ref field, value); } = string.Empty;

    /// <summary>The description as a list of items (the view never parses HTML).</summary>
    public IReadOnlyList<string> DescriptionItems { get; set => Set(ref field, value); } = [];

    public string PriceLabel { get; set => Set(ref field, value); } = string.Empty;

    public string ImageUrl { get; set => Set(ref field, value); } = string.Empty;

    public string? ErrorMessage { get; set => Set(ref field, value); }

    /// <summary>The quantity chosen in the view.</summary>
    public Func<int, Task> OnAddToCart { get; set; } = ScopeActions.Unbound<int>();

    public Func<Task> OnBack { get; set; } = ScopeActions.Unbound();
}

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
            await App.FlipToIntent(intent.Redirect(Places.Home)); // nothing to show: back to the panels
            return false;
        }

        if (initialization)
        {
            Scope.OnAddToCart = Action<int>(OnAddToCart);
            Scope.OnBack = Action(() => Flip(Places.Home));
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
        await Flip(Places.Cart);
    }
}
