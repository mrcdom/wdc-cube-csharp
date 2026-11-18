using Avalonia.Controls;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.Cart;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class CartView : ScopeView
{
    public CartView() => InitializeComponent();

    private CartScope S => (CartScope)Scope!;

    private void OnContinue(object? sender, RoutedEventArgs e) => _ = S.OnContinue();

    private void OnBuy(object? sender, RoutedEventArgs e) => _ = S.OnBuy();
}
