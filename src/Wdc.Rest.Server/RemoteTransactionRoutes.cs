using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// <c>POST /api/tx/begin</c> → <c>{"txId":"…"}</c>; <c>POST /api/tx/commit</c> and <c>/rollback</c> with
/// <c>X-Tx-Id</c> → <c>{"status":"committed"|"rolledback"}</c>; <c>GET /api/tx/status</c> →
/// <c>{"status":"open"|"committed"|"rolledback"|"unknown"}</c>.
/// </summary>
public static class RemoteTransactionRoutes
{
    public static RouteGroupBuilder MapRemoteTransactions(this IEndpointRouteBuilder routes, string basePath = RestNames.TransactionBase)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var group = routes.MapGroup(basePath);
        group.MapPost("begin", context => RestHttp.Respond(context, async () =>
        {
            var id = await Coordinator(context).Begin(Owner(context), context.RequestAborted);
            return RestJson.Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("txId", id);
                w.WriteEndObject();
            });
        }));
        group.MapPost("commit", context => RestHttp.Respond(context, async () =>
            StatusBody(await Coordinator(context).Commit(Id(context), Owner(context), context.RequestAborted))));
        group.MapPost("rollback", context => RestHttp.Respond(context, async () =>
            StatusBody(await Coordinator(context).Rollback(Id(context), Owner(context), context.RequestAborted))));
        group.MapGet("status", Status);
        return group;
    }

    private static Task Status(HttpContext context) =>
        RestHttp.Respond(context, StatusOf(context));

    private static Func<Task<byte[]?>> StatusOf(HttpContext context) =>
        () => Task.FromResult<byte[]?>(StatusBody(Coordinator(context).Status(Id(context), Owner(context))));

    /// <summary>The wire name of a status.</summary>
    public static string WireName(RemoteTransactionStatus status) => status switch
    {
        RemoteTransactionStatus.Open => "open",
        RemoteTransactionStatus.Committed => "committed",
        RemoteTransactionStatus.RolledBack => "rolledback",
        _ => "unknown",
    };

    private static byte[] StatusBody(RemoteTransactionStatus status) => RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WriteString("status", WireName(status));
        w.WriteEndObject();
    });

    private static RemoteTransactionCoordinator Coordinator(HttpContext context) =>
        context.RequestServices.GetService<RemoteTransactionCoordinator>()
        ?? throw new InvalidRequestException("Remote transactions are not enabled on this server.");

    private static string? Owner(HttpContext context) => RestHttp.Options(context).OwnerOf(context);

    private static string Id(HttpContext context)
    {
        var id = context.Request.Headers[RestNames.TransactionHeader].ToString();
        return id.Length > 0 ? id : throw new InvalidRequestException($"The {RestNames.TransactionHeader} header is required.");
    }
}

/// <summary>Runs <see cref="RemoteTransactionCoordinator.Reap"/> every <see cref="RemoteTransactionOptions.ReapInterval"/>.</summary>
public sealed class RemoteTransactionReaper(RemoteTransactionCoordinator coordinator, TimeProvider? time = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(coordinator.Options.ReapInterval, time ?? TimeProvider.System);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await coordinator.Reap(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

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
