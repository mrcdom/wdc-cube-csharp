using System.Net;
using System.Text;

namespace Wdc.Rest.Testing;

/// <summary>The answer to a raw request of <see cref="RestTestHost"/>.</summary>
public sealed class RestTestResponse(HttpStatusCode status, byte[] body, IReadOnlyDictionary<string, string> headers)
{
    public HttpStatusCode Status => status;

    /// <summary>The body as sent (the test host does not decompress it).</summary>
    public ReadOnlyMemory<byte> Bytes => body;

    /// <summary>The body as UTF-8 text.</summary>
    public string Body => Encoding.UTF8.GetString(body);

    /// <summary>The message of an error body (<c>{"error":"…"}</c>), or <see langword="null"/>.</summary>
    public string? Error => RestErrors.MessageOf(body);

    /// <summary>Response and content headers, by name in any case; repeated values joined with <c>, </c>.</summary>
    public IReadOnlyDictionary<string, string> Headers => headers;

    /// <summary>A header's value, or <see langword="null"/>.</summary>
    public string? Header(string name) => headers.TryGetValue(name, out var value) ? value : null;
}
