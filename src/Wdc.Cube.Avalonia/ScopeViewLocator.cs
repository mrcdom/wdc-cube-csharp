using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Wdc.Cube.Avalonia;

/// <summary>
/// Which control draws each scope type (as <c>ViewRegistry</c> does in Blazor). Install it in the application's
/// <c>DataTemplates</c>; a <c>ContentControl</c> whose content is a scope then draws it with its view. A new scope
/// instance always gets a new control (like Blazor's <c>@key</c> by instance): no state leaks between faces.
/// </summary>
public sealed class ScopeViewLocator : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _views = [];

    /// <summary>Registers the control that draws <typeparamref name="TScope"/>.</summary>
    public ScopeViewLocator Register<TScope>(Func<Control> create)
        where TScope : Scope
    {
        ArgumentNullException.ThrowIfNull(create);
        _views[typeof(TScope)] = create;
        return this;
    }

    public bool Match(object? data) => data is Scope;

    public Control? Build(object? param)
    {
        if (param is not Scope scope)
        {
            return null;
        }

        return _views.TryGetValue(scope.GetType(), out var create)
            ? create()
            : new TextBlock { Text = $"No view registered for {scope.GetType().Name}", Classes = { "cube-missing-view" } };
    }
}
