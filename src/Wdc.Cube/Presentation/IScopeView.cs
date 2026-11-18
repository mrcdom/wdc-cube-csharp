namespace Wdc.Cube;

/// <summary>
/// The whole boundary between a scope and the technology that draws it. Called on the application's dispatcher,
/// during the flush, when the scope changed.
/// </summary>
/// <remarks>
/// The view redraws however its technology does it (a reactive framework schedules a render, an imperative one
/// patches only what changed) and then calls <see cref="Scope.MarkRendered"/>. Until it does, the scope is not
/// notified again: later changes wait for the next window.
/// </remarks>
public interface IScopeView
{
    /// <summary>The scope changed: redraw it, then call <see cref="Scope.MarkRendered"/>.</summary>
    void Invalidate(Scope scope);
}
