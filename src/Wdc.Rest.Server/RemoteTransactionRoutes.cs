using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// <c>POST /api/tx/begin</c> → <c>{"txId":"…"}</c>; <c>POST /api/tx/commit</c> with <c>X-Tx-Id</c> →
/// <c>{"status":"committed"}</c> (409 when a request inside failed); <c>/rollback</c> → <c>{"status":"rolledback"}</c>;
/// <c>GET /api/tx/status</c> →
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
            var id = await Coordinator(context).Begin(Owner(context), Quota(context), context.RequestAborted);
            return RestJson.Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("txId", id);
                w.WriteEndObject();
            });
        })).WithMetadata(new RestRouteMetadata(RestRoute.TransactionBegin));
        group.MapPost("commit", context => RestHttp.Respond(context, async () =>
            StatusBody(await Coordinator(context).Commit(Id(context), Owner(context), context.RequestAborted))))
            .WithMetadata(new RestRouteMetadata(RestRoute.TransactionCommit));
        group.MapPost("rollback", context => RestHttp.Respond(context, async () =>
            StatusBody(await Coordinator(context).Rollback(Id(context), Owner(context), context.RequestAborted))))
            .WithMetadata(new RestRouteMetadata(RestRoute.TransactionRollback));
        group.MapGet("status", Status).WithMetadata(new RestRouteMetadata(RestRoute.TransactionStatus));
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

    private static string? Quota(HttpContext context) => RestHttp.Options(context).QuotaOf(context);

    private static string Id(HttpContext context)
    {
        var id = context.Request.Headers[RestNames.TransactionHeader].ToString();
        return id.Length > 0 ? id : throw new InvalidRequestException($"The {RestNames.TransactionHeader} header is required.");
    }
}
