using Microsoft.AspNetCore.Components;

namespace Wdc.Cube.Blazor;

/// <summary>
/// Base of a component that draws a scope. It binds itself as the scope's <see cref="IScopeView"/>: when the scope
/// is flushed it re-renders, and after rendering it confirms with <see cref="Scope.MarkRendered"/>, so the scope
/// becomes clean only when the screen shows it (decision D-03).
/// </summary>
/// <remarks>
/// <para>
/// Read the scope's properties in the markup and call its actions from event handlers. A view only draws: what
/// another view technology would write the same way (formatting, validation, derived values) belongs in the scope or
/// the presenter.
/// </para>
/// <para>
/// Events do not trigger a render by themselves (unlike a plain <see cref="ComponentBase"/>): the scope decides. A
/// guarded action flushes when it ends, so its result shows at once; typing mirrors show in the next window. A view
/// that keeps state of its own (an open menu, focus) calls <see cref="ComponentBase.StateHasChanged"/> itself.
/// </para>
/// </remarks>
public abstract class ScopeView<TScope> : ComponentBase, IScopeView, IHandleEvent, IDisposable
    where TScope : Scope
{
    private IDisposable? _binding;
    private TScope? _bound;
    private bool _renderOwed;

    /// <summary>The scope this component draws.</summary>
    [Parameter]
    [EditorRequired]
    public TScope Scope { get; set; } = null!;

    /// <inheritdoc/>
    public void Dispose()
    {
        Unbind();
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg) => callback.InvokeAsync(arg);

    /// <inheritdoc/>
    void IScopeView.Invalidate(Scope scope)
    {
        _renderOwed = true;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Scope, _bound))
        {
            Unbind();
            _bound = Scope ?? throw new InvalidOperationException($"{GetType().Name} needs a Scope.");
            _binding = Scope.Attach(this);
        }

        base.OnParametersSet();
    }

    /// <inheritdoc/>
    protected override void OnAfterRender(bool firstRender)
    {
        if (_renderOwed)
        {
            _renderOwed = false;
            _bound?.MarkRendered();
        }

        base.OnAfterRender(firstRender);
    }

    /// <summary>Releases what the derived view holds.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }

    private void Unbind()
    {
        _binding?.Dispose(); // a scope still waiting for this view becomes clean: nobody is left to confirm
        _binding = null;
        _bound = null;
        _renderOwed = false;
    }
}
