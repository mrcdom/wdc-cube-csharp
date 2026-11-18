using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.Receipt;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class ReceiptView : ScopeView
{
    public ReceiptView() => InitializeComponent();

    private ReceiptScope S => (ReceiptScope)Scope!;

    private void OnContinue(object? sender, RoutedEventArgs e) => _ = S.OnContinue();
}
