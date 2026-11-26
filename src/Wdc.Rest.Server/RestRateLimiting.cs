using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>ASP.NET's rate limiter answering in the contract's terms.</summary>
public static class RestRateLimiting
{
    /// <summary>
    /// For <see cref="RateLimiterOptions.OnRejected"/>: 429 with <c>{"error":"…"}</c> and <c>Retry-After</c> when the
    /// limiter knows it (fixed and sliding windows, token buckets), with the REST headers of every answer.
    /// </summary>
    public static ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        TimeSpan? wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : null;
        return new ValueTask(RestHttp.WriteError(context.HttpContext,
            new TooManyRequestsException("Too many requests: try again later.", wait)));
    }
}
