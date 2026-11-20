using Avalonia.Controls;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.Admin;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class AdminView : ScopeView
{
    public AdminView() => InitializeComponent();

    private AdminScope S => (AdminScope)Scope!;

    private AdminScope? _watched;

    // What is being typed: view plumbing. The presenter receives it on "Filtrar" (or Enter); the filter in force
    // (from the address too) is shown when it changes.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnScopeChanged;
        }

        _watched = Scope as AdminScope;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnScopeChanged;
            CustomerBox.Text = _watched.CustomerFilter;
        }
    }

    private void OnScopeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdminScope.CustomerFilter) && sender is AdminScope scope)
        {
            CustomerBox.Text = scope.CustomerFilter;
        }
    }

    private void OnFilter(object? sender, RoutedEventArgs e) => _ = S.OnFilter(CustomerBox.Text ?? string.Empty);

    private void OnCustomerKey(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == global::Avalonia.Input.Key.Enter)
        {
            OnFilter(sender, e);
        }
    }

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AdminPurchaseRow row })
        {
            _ = S.OnOpenPurchase(row.Id);
        }
    }

    private void OnPrevious(object? sender, RoutedEventArgs e) => _ = S.OnPrevious();

    private void OnNext(object? sender, RoutedEventArgs e) => _ = S.OnNext();

    private void OnBack(object? sender, RoutedEventArgs e) => _ = S.OnBack();
}
