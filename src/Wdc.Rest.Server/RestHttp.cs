using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>Server options of the REST contract.</summary>
public sealed class RestServerOptions
{
    /// <summary>Largest request body read (bytes).</summary>
    public int MaxBodyBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Largest number of items one <c>fetch</c> returns, and the largest <c>pageSize</c>. A <c>fetch</c> with no limit
    /// that would return more is refused (400) instead of silently cut.
    /// </summary>
    public int MaxItems { get; set; } = 10_000;

    /// <summary>
    /// Refuses (409) a write without <c>X-Tx-Id</c> while the same owner has a remote transaction open: it is almost
    /// always a lost header, and the write would bypass the transaction.
    /// </summary>
    public bool RefuseWritesOutsideOpenTransaction { get; set; } = true;

    /// <summary>Who owns a remote transaction: the user (when authenticated) and the client instance.</summary>
    public Func<HttpContext, string?> OwnerOf { get; set; } = DefaultOwner;

    /// <summary><c>user:&lt;id&gt;|client:&lt;X-Client-Id&gt;</c>; either part may be missing; neither → no owner.</summary>
    public static string? DefaultOwner(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
            : null;
        var client = context.Request.Headers[RestNames.ClientHeader].ToString();
        return (user, client.Length == 0 ? null : client) switch
        {
            (null, null) => null,
            (var u, var c) => $"user:{u}|client:{c}",
        };
    }
}

/// <summary>Reading bodies, writing JSON answers and turning exceptions into the contract's errors.</summary>
public static class RestHttp
{
    private const string Json = "application/json; charset=utf-8";

    public static async Task<byte[]> ReadBody(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var max = Options(context).MaxBodyBytes;
        if (context.Request.ContentLength > max)
        {
            throw new InvalidRequestException($"The body is larger than {max} bytes.");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            if (buffer.Length + read > max)
            {
                throw new InvalidRequestException($"The body is larger than {max} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Runs <paramref name="handle"/> and writes its JSON answer (200), or the error the contract defines.</summary>
    public static async Task Respond(HttpContext context, Func<Task<byte[]?>> handle)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handle);
        byte[]? body;
        try
        {
            body = await handle();
        }
        catch (Exception caught) when (!context.RequestAborted.IsCancellationRequested || caught is not OperationCanceledException)
        {
            await WriteError(context, caught);
            return;
        }

        if (body is null)
        {
            await WriteError(context, new KeyNotFoundException("Not found"));
            return;
        }

        await Write(context, HttpStatusCode.OK, body);
    }

    public static Task Write(HttpContext context, HttpStatusCode status, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(body);
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = Json;
        context.Response.ContentLength = body.Length;
        return context.Response.Body.WriteAsync(body, context.RequestAborted).AsTask();
    }

    public static Task WriteError(HttpContext context, Exception error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);
        var (status, message) = RestErrors.ToResponse(error);
        if (status >= HttpStatusCode.InternalServerError)
        {
            Logger(context).LogError(error, "{Method} {Path} failed.", context.Request.Method, context.Request.Path);
        }

        return context.Response.HasStarted ? Task.CompletedTask : Write(context, status, RestErrors.Body(message));
    }

    internal static RestServerOptions Options(HttpContext context) =>
        context.RequestServices.GetService<RestServerOptions>() ?? s_defaults;

    private static readonly RestServerOptions s_defaults = new();

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Wdc.Rest.Server") ??
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
