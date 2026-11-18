using System.Diagnostics.CodeAnalysis;

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
