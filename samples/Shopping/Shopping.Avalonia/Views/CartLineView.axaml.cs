using Avalonia.Controls;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.Cart;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class CartLineView : ScopeView
{
    public CartLineView() => InitializeComponent();

    private CartLineScope S => (CartLineScope)Scope!;

    private void OnMinus(object? sender, RoutedEventArgs e) => _ = S.OnChangeQuantity(S.Quantity - 1);

    private void OnPlus(object? sender, RoutedEventArgs e) => _ = S.OnChangeQuantity(S.Quantity + 1);

    private void OnRemove(object? sender, RoutedEventArgs e) => _ = S.OnRemove();
}
