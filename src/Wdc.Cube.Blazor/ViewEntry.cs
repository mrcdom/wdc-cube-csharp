using System.Diagnostics.CodeAnalysis;

namespace Wdc.Cube.Blazor;

/// <summary>A registered view component.</summary>
public sealed class ViewEntry([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType)
{
    /// <summary>The component type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type ComponentType { get; } = componentType;
}
