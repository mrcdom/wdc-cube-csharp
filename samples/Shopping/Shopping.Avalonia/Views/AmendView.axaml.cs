using Avalonia.Controls;
using Avalonia.Interactivity;
using Shopping.Presentation.Modules.Home.Admin.Amend;
using Wdc.Cube.Avalonia;

namespace Shopping.Avalonia.Views;

public sealed partial class AmendView : ScopeView
{
    private AmendScope? _watched;
    private bool _syncing;

    public AmendView() => InitializeComponent();

    private AmendScope S => (AmendScope)Scope!;

    /// <summary>The reason goes to the presenter as it is typed; a reason cleared there (after saving) clears the box.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnScopeChanged;
        }

        _watched = Scope as AmendScope;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnScopeChanged;
            ShowReason(_watched.Reason);
        }
    }

    private void OnScopeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AmendScope.Reason) && sender is AmendScope scope)
        {
            ShowReason(scope.Reason);
        }
    }

    private void ShowReason(string reason)
    {
        if (ReasonBox.Text != reason)
        {
            _syncing = true;
            ReasonBox.Text = reason;
            _syncing = false;
        }
    }

    private void OnReasonChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_syncing && Scope is AmendScope scope)
        {
            _ = scope.OnChangeReason(ReasonBox.Text ?? string.Empty);
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => _ = S.OnConfirm();

    private void OnBack(object? sender, RoutedEventArgs e) => _ = S.OnBack();
}
