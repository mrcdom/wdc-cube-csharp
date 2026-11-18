using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Wdc.Cube.Blazor;

/// <summary>
/// Draws whatever scope sits in a slot, with the view registered for its type. The view is keyed by the scope
/// instance: updating the same scope redraws the view in place; putting another scope in the slot replaces the view.
/// </summary>
public sealed class ViewSlot : ComponentBase
{
    /// <summary>The scope to draw; nothing (or <see cref="Empty"/>) when <see langword="null"/>.</summary>
    [Parameter]
    public Scope? Scope { get; set; }

    /// <summary>What to draw when the slot is empty.</summary>
    [Parameter]
    public RenderFragment? Empty { get; set; }

    [Inject]
    private ViewRegistry Registry { get; set; } = null!;

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (Scope is null)
        {
            builder.AddContent(0, Empty);
            return;
        }

        if (Registry.Find(Scope.GetType()) is not { } view)
        {
            // Visible on screen, rather than a blank area that says nothing about what is missing.
            builder.OpenElement(1, "div");
            builder.AddAttribute(2, "class", "cube-missing-view");
            builder.AddContent(3, $"No view registered for {Scope.GetType().Name}");
            builder.CloseElement();
            return;
        }

        builder.OpenComponent(4, view.ComponentType);
        builder.SetKey(Scope);
        builder.AddComponentParameter(5, nameof(ScopeView<Scope>.Scope), Scope);
        builder.CloseComponent();
    }
}
