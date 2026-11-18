using Microsoft.Extensions.DependencyInjection;

namespace Wdc.Cube.Blazor;

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
