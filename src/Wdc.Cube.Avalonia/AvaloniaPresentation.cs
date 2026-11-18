using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;

namespace Wdc.Cube.Avalonia;

/// <summary>Bridges a Cube application to Avalonia.</summary>
public static class AvaloniaPresentation
{
    /// <summary>
    /// The presentation dispatcher on Avalonia's UI thread (decision D-01 allows the UI's own): presenters, flushes and
    /// views share one thread, and <c>await</c> comes back to it through Avalonia's synchronization context.
    /// </summary>
    public static PresentationDispatcher CreateDispatcher() =>
        PresentationDispatcher.FromHost(action => Dispatcher.UIThread.Post(action), Dispatcher.UIThread.CheckAccess);
}

/// <summary>
/// Which control draws each scope type (the <c>ViewRegistry</c> of decision D-04). Install it in the application's
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
