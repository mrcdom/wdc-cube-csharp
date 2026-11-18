using Microsoft.Extensions.DependencyInjection;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>Registration.</summary>
public static class RestServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the REST server options and, when <paramref name="remoteTransactions"/>, the coordinator and its reaper
    /// (it needs an <see cref="IDetachedTransactionSource"/> in the container: the database).
    /// </summary>
    public static IServiceCollection AddCubeRest(this IServiceCollection services, Action<RestServerOptions>? configure = null,
        bool remoteTransactions = true, Action<RemoteTransactionOptions>? configureTransactions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new RestServerOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<RpcRegistry>();
        if (remoteTransactions)
        {
            var txOptions = new RemoteTransactionOptions();
            configureTransactions?.Invoke(txOptions);
            services.AddSingleton(txOptions);
            services.AddSingleton(sp => new RemoteTransactionCoordinator(
                sp.GetRequiredService<IDetachedTransactionSource>(), txOptions, sp.GetService<TimeProvider>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<RemoteTransactionCoordinator>>()));
            services.AddHostedService(sp => new RemoteTransactionReaper(
                sp.GetRequiredService<RemoteTransactionCoordinator>(), sp.GetService<TimeProvider>()));
        }

        return services;
    }
}
