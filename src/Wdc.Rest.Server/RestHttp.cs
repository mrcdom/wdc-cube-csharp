using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wdc.Domain;

namespace Wdc.Rest.Server;

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
