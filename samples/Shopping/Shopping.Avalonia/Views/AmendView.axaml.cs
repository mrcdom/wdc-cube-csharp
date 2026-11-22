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

    // Watched only while shown: a view that leaves the tree no longer hangs on its scope's event.
    private bool _attached;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Watch(_attached ? Scope as AmendScope : null);
    }

    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Watch(Scope as AmendScope);
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The reason goes to the presenter as it is typed; a reason cleared there (after saving) clears the box.</summary>
    private void Watch(AmendScope? scope)
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
            ShowReason(scope.Reason);
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
