using Avalonia.Controls;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.ProductDetail;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class ProductView : ScopeView
{
    public ProductView() => InitializeComponent();

    private ProductScope S => (ProductScope)Scope!;

    // The quantity being chosen: view plumbing, sent to the presenter on "Adicionar".
    private int _quantity = 1;

    private void ChangeQuantity(int delta)
    {
        _quantity = Math.Max(1, _quantity + delta);
        Quantity.Text = _quantity.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    private void OnMinus(object? sender, RoutedEventArgs e) => ChangeQuantity(-1);

    private void OnPlus(object? sender, RoutedEventArgs e) => ChangeQuantity(+1);

    private void OnBack(object? sender, RoutedEventArgs e) => _ = S.OnBack();

    private void OnAdd(object? sender, RoutedEventArgs e) => _ = S.OnAddToCart(_quantity);
}
