using Shopping.I18n;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Home.Products.Calls;
using Wdc.Cube;

namespace Shopping.Presentation.Modules.Home.Products;

/// <summary>A child of Home (no place): the catalogue, by name.</summary>
public sealed class ProductsPanelPresenter : Presenter<RootPresenter, ProductsPanelScope>
{
    public const int MaxProducts = 1000;

    private readonly Func<long, Task> _openProduct;
    private int _pending;

    public ProductsPanelPresenter(RootPresenter app, Func<long, Task> openProduct)
        : base(app, new ProductsPanelScope())
    {
        _openProduct = openProduct;
        Scope.OnOpenProduct = Action<long>(OnOpenProduct);
    }

    public async Task Load()
    {
        var token = ++_pending;
        Scope.Loading = true;
        try
        {
            var products = await new ProductListQuery(App.Repositories, MaxProducts).Run();
            if (token != _pending || IsReleased)
            {
                return;
            }

            Scope.Products =
            [
                .. products.Select(p => new ProductCard(p.Id!.Value, p.Name ?? string.Empty, Formats.Money(p.Price ?? 0),
                    Formats.ProductImageUrl(p.Id!.Value))),
            ];
            Scope.CountLabel = Texts.CommonItemCount(count: products.Count);
        }
        catch (Exception caught)
        {
            App.Unexpected("Loading the products", caught);
        }
        finally
        {
            if (token == _pending && !IsReleased)
            {
                Scope.Loading = false;
            }
        }
    }

    private Task OnOpenProduct(long productId) => _openProduct(productId); // the owner decides where it goes
}
