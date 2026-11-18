using Avalonia.Controls;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.Products;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class ProductsPanelView : ScopeView
{
    public ProductsPanelView() => InitializeComponent();

    private ProductsPanelScope S => (ProductsPanelScope)Scope!;

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ProductCard card })
        {
            _ = S.OnOpenProduct(card.Id);
        }
    }
}
