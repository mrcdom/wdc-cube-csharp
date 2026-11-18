using Shopping.Domain.Products;
using Shopping.Presentation.Infra;
using Shopping.Presentation.Modules.Main;
using Wdc.Cube;
using Wdc.Domain;

namespace Shopping.Presentation.Modules.Home.Products;

/// <summary>A child of Home (no place): the catalogue, by name.</summary>
public sealed class ProductsPanelPresenter : Presenter<MainPresenter, ProductsPanelScope>
{
    public const int MaxProducts = 1000;

    private int _pending;

    public ProductsPanelPresenter(MainPresenter app, Func<long, Task> openProduct)
        : base(app, new ProductsPanelScope())
    {
        Scope.OnOpenProduct = Action<long>(openProduct);
    }

    public async Task Load()
    {
        var token = ++_pending;
        Scope.Loading = true;
        try
        {
            var criteria = new ProductCriteria()
                .WithOrderBy(ProductOrderBy.NameAToZ)
                .WithProjection(new Product { Id = Mark.Int64, Name = Mark.String, Price = Mark.Decimal });
            var products = await App.Repositories.Products.Fetch(criteria, limit: MaxProducts);
            if (token != _pending || IsReleased)
            {
                return;
            }

            Scope.Products =
            [
                .. products.Select(p => new ProductCard(p.Id!.Value, p.Name ?? string.Empty, Formats.Money(p.Price ?? 0),
                    Formats.ProductImageUrl(p.Id!.Value))),
            ];
            Scope.CountLabel = $"{products.Count} {(products.Count == 1 ? "item" : "itens")}";
        }
        catch (Exception caught)
        {
            App.Unexpected("Carregando os produtos", caught);
        }
        finally
        {
            if (token == _pending && !IsReleased)
            {
                Scope.Loading = false;
            }
        }
    }
}
