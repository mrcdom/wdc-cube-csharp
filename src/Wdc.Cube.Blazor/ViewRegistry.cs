using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Wdc.Cube.Blazor;

/// <summary>
/// Which component draws each scope type. One registry per renderer: a scope can have a Blazor view and another
/// technology's view at the same time.
/// </summary>
public sealed class ViewRegistry
{
    private readonly Dictionary<Type, ViewEntry> _views = [];

    /// <summary>Registers <typeparamref name="TView"/> as the view of <typeparamref name="TScope"/>.</summary>
    public ViewRegistry Register<TScope,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TView>()
        where TScope : Scope
        where TView : ScopeView<TScope>
    {
        _views[typeof(TScope)] = new ViewEntry(typeof(TView));
        return this;
    }

    /// <summary>
    /// The view registered for <paramref name="scopeType"/>, or for the nearest base type that has one; or
    /// <see langword="null"/>.
    /// </summary>
    public ViewEntry? Find(Type scopeType)
    {
        ArgumentNullException.ThrowIfNull(scopeType);
        for (var type = scopeType; type is not null && type != typeof(Scope); type = type.BaseType)
        {
            if (_views.TryGetValue(type, out var entry))
            {
                return entry;
            }
        }

        return null;
    }
}

/// <summary>A registered view component.</summary>
public sealed class ViewEntry([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType)
{
    /// <summary>The component type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type ComponentType { get; } = componentType;
}

/// <summary>Registration helpers.</summary>
public static class CubeBlazorServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="ViewRegistry"/> filled by <paramref name="register"/>.</summary>
    public static IServiceCollection AddCubeViews(this IServiceCollection services, Action<ViewRegistry> register)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(register);
        var registry = new ViewRegistry();
        register(registry);
        return services.AddSingleton(registry);
    }
}

/// <summary>Builds a <see cref="PresentationDispatcher"/> over Blazor's renderer dispatcher.</summary>
public static class BlazorPresentation
{
    /// <summary>
    /// The presentation runs on the renderer's own serial context (the circuit in Blazor Server, the single thread
    /// in WebAssembly), so scope changes, flushes and renders never cross threads.
    /// </summary>
    public static PresentationDispatcher CreateDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return PresentationDispatcher.FromHost(action => _ = dispatcher.InvokeAsync(action), dispatcher.CheckAccess);
    }
}
