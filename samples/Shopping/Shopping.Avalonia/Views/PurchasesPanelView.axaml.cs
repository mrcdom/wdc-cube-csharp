using Avalonia.Controls;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.Purchases;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class PurchasesPanelView : ScopeView
{
    public PurchasesPanelView() => InitializeComponent();

    private PurchasesPanelScope S => (PurchasesPanelScope)Scope!;

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PurchaseCard card })
        {
            _ = S.OnOpenReceipt(card.Id);
        }
    }

    private void OnPrevious(object? sender, RoutedEventArgs e) => _ = S.OnPrevious();

    private void OnNext(object? sender, RoutedEventArgs e) => _ = S.OnNext();
}
