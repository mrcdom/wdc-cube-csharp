using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Wdc.Cube.Avalonia;

/// <summary>
/// Base of a control that draws a scope (its <c>DataContext</c>). It binds itself as the scope's
/// <see cref="IScopeView"/> while on screen and confirms each flush with <see cref="Scope.MarkRendered"/> after
/// Avalonia has laid out and rendered, so the scope becomes clean only when the screen shows it.
/// </summary>
/// <remarks>
/// Bind the markup to the scope's properties (compiled bindings, <c>x:DataType</c>): the scope raises
/// <c>PropertyChanged</c> at the flush, all changes of a window at once. Call the scope's actions from event handlers
/// or commands; a view only draws.
/// </remarks>
public class ScopeView : UserControl, IScopeView
{
    private IDisposable? _binding;
    private Scope? _bound;
    private bool _onScreen;

    /// <summary>The scope drawn.</summary>
    public Scope? Scope => DataContext as Scope;

    void IScopeView.Invalidate(Scope scope)
    {
        // After the layout and render passes of this frame (Background runs after Render).
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(scope, _bound))
            {
                scope.MarkRendered();
            }
        }, DispatcherPriority.Background);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_onScreen)
        {
            Bind();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _onScreen = true;
        Bind();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _onScreen = false;
        Unbind();
        base.OnDetachedFromVisualTree(e);
    }

    private void Bind()
    {
        var scope = Scope;
        if (ReferenceEquals(scope, _bound))
        {
            return;
        }

        Unbind();
        if (scope is not null)
        {
            _bound = scope;
            _binding = scope.Attach(this);
        }
    }

    private void Unbind()
    {
        _binding?.Dispose();
        _binding = null;
        _bound = null;
    }
}
