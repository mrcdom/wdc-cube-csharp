using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class AmendLineView : ScopeView
{
    public AmendLineView() => InitializeComponent();

    private AmendLineScope S => (AmendLineScope)Scope!;

    private void OnMinus(object? sender, RoutedEventArgs e) => _ = S.OnChangeNewQuantity(S.NewQuantity - 1);

    private void OnPlus(object? sender, RoutedEventArgs e) => _ = S.OnChangeNewQuantity(S.NewQuantity + 1);
}
