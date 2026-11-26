using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
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
        RestTiming.Start(context);
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
        AddHeaders(context);
        context.Response.ContentType = Json;
        context.Response.ContentLength = body.Length;
        return context.Response.Body.WriteAsync(body, context.RequestAborted).AsTask();
    }

    public static Task WriteError(HttpContext context, Exception error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);
        var answer = Options(context).Errors.Resolve(error);
        if (answer.Status >= HttpStatusCode.InternalServerError)
        {
            Logger(context).LogError(error, "{Method} {Path} failed.", context.Request.Method, context.Request.Path);
        }

        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        foreach (var (name, value) in answer.Headers ?? s_noHeaders)
        {
            context.Response.Headers[name] = value;
        }

        return Write(context, answer.Status, RestErrors.Body(answer.Message));
    }

    private static readonly Dictionary<string, string> s_noHeaders = [];

    private static void AddHeaders(HttpContext context)
    {
        var options = Options(context);
        var headers = context.Response.Headers;
        foreach (var (name, value) in options.SecurityHeaders)
        {
            headers[name] = value;
        }

        if (options.CacheControl is { } cacheControl && !headers.ContainsKey(HeaderNames.CacheControl))
        {
            headers.CacheControl = cacheControl;
        }

        if (options.ServerTiming && RestTiming.Header(context, options.ServerTimingDescriptions) is { } timing)
        {
            headers["Server-Timing"] = timing;
        }
    }

    internal static RestServerOptions Options(HttpContext context) =>
        context.RequestServices.GetService<RestServerOptions>() ?? s_defaults;

    private static readonly RestServerOptions s_defaults = new();

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Wdc.Rest.Server") ??
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
