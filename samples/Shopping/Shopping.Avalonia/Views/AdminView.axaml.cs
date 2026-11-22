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
    // (from the address too) is shown when it changes. Watched only while shown: a view that leaves the tree no longer hangs on its scope's event.
    private bool _attached;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Watch(_attached ? Scope as AdminScope : null);
    }

    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Watch(Scope as AdminScope);
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Watch(AdminScope? scope)
    {
        if (ReferenceEquals(scope, _watched))
        {
            return;
        }

        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnScopeChanged;
        }

        _watched = scope;
        if (scope is not null)
        {
            scope.PropertyChanged += OnScopeChanged;
            CustomerBox.Text = scope.CustomerFilter;
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
