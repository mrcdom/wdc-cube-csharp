using System.Net;

namespace Wdc.Rest.Server;

/// <summary>An error answer: its status, the message of <c>{"error":"…"}</c>, and headers to add.</summary>
public sealed record RestError(HttpStatusCode Status, string Message)
{
    /// <summary>Headers sent with the answer (<c>Retry-After</c>, <c>WWW-Authenticate</c>...).</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}
