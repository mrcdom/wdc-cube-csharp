using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class HomeView : ScopeView
{
    public HomeView() => InitializeComponent();

    private HomeScope S => (HomeScope)Scope!;

    private void OnHome(object? sender, RoutedEventArgs e) => _ = S.OnHome();

    private void OnCart(object? sender, RoutedEventArgs e) => _ = S.OnOpenCart();

    private void OnExit(object? sender, RoutedEventArgs e) => _ = S.OnExit();
}
