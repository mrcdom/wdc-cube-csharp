using System.Net;
using System.Net.Http.Headers;

namespace Wdc.Rest.Client;

/// <summary>
/// Sends the calls of the contract: JSON in and out, <c>X-Client-Id</c>, the bearer token, and <c>X-Tx-Id</c> when the
/// calling flow is inside a remote transaction. An error answer becomes the exception of <see cref="RestErrors"/>.
/// </summary>
public sealed class RestClient
{
    private static readonly MediaTypeHeaderValue s_json = new("application/json") { CharSet = "utf-8" };
    private readonly HttpClient _http;
    private readonly RestClientOptions _options;

    /// <param name="http">With <see cref="HttpClient.BaseAddress"/> set to the server.</param>
    /// <param name="options">Authentication and client id; defaults to none and a new id.</param>
    public RestClient(HttpClient http, RestClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _options = options ?? new RestClientOptions();
    }

    public string ClientId => _options.ClientId;

    /// <summary>The remote transaction of the calling flow (follows <c>await</c>, like the local one).</summary>
    internal AsyncLocal<RemoteTransactionState?> Ambient { get; } = new();

    internal string? CurrentTransactionId => Ambient.Value is { Active: true } state ? state.Id : null;

    public async Task<byte[]> Post(string path, byte[] body, CancellationToken cancellationToken = default) =>
        (await Send(HttpMethod.Post, path, body, nullOnNotFound: false, CurrentTransactionId, cancellationToken).ConfigureAwait(false))!;

    /// <summary>Like <see cref="Post"/>, with 404 (and 204) as <see langword="null"/>.</summary>
    public Task<byte[]?> PostOrNull(string path, byte[] body, CancellationToken cancellationToken = default) =>
        Send(HttpMethod.Post, path, body, nullOnNotFound: true, CurrentTransactionId, cancellationToken);

    /// <summary>A GET; an error status throws (404 as <see cref="KeyNotFoundException"/>), and 204 is an empty body.</summary>
    public async Task<byte[]> Get(string path, CancellationToken cancellationToken = default) =>
        (await Send(HttpMethod.Get, path, null, nullOnNotFound: false, CurrentTransactionId, cancellationToken).ConfigureAwait(false))!;

    /// <summary>A GET; 404 (and 204) as <see langword="null"/>.</summary>
    public Task<byte[]?> GetOrNull(string path, CancellationToken cancellationToken = default) =>
        Send(HttpMethod.Get, path, null, nullOnNotFound: true, CurrentTransactionId, cancellationToken);

    /// <summary>A call outside the ambient transaction, with an explicit one (the transaction routes).</summary>
    internal Task<byte[]?> SendWithTransaction(HttpMethod method, string path, string? transactionId, CancellationToken cancellationToken) =>
        Send(method, path, method == HttpMethod.Get ? null : [], nullOnNotFound: false, transactionId, cancellationToken);

    private async Task<byte[]?> Send(HttpMethod method, string path, byte[]? body, bool nullOnNotFound, string? transactionId,
        CancellationToken cancellationToken)
    {
        var response = await SendOnce(method, path, body, transactionId, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized && _options.RefreshAccess is { } refresh)
        {
            response.Dispose();
            if (await refresh(cancellationToken).ConfigureAwait(false))
            {
                response = await SendOnce(method, path, body, transactionId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw RestErrors.FromResponse(HttpStatusCode.Unauthorized, "The session expired.");
            }
        }

        using (response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response.StatusCode == HttpStatusCode.NoContent && nullOnNotFound ? null : bytes;
            }

            if (response.StatusCode == HttpStatusCode.NotFound && nullOnNotFound)
            {
                return null;
            }

            throw RestErrors.FromResponse(response.StatusCode, RestErrors.MessageOf(bytes), RetryAfter(response));
        }
    }

    // Seconds or a date; a date already past is "now".
    private static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - DateTimeOffset.UtcNow is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero,
        _ => null,
    };

    private async Task<HttpResponseMessage> SendOnce(HttpMethod method, string path, byte[]? body, string? transactionId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = s_json;
        }

        request.Headers.Add(RestNames.ClientHeader, _options.ClientId);
        if (transactionId is not null)
        {
            request.Headers.Add(RestNames.TransactionHeader, transactionId);
        }

        if (_options.AccessToken is { } token && await token(cancellationToken).ConfigureAwait(false) is { } value)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        }

        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
