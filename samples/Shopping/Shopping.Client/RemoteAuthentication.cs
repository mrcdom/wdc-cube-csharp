using System.Net;
using System.Text.Json;
using Shopping.Domain.Users;
using Shopping.Presentation.Infra;
using Wdc.Rest;
using Wdc.Rest.Client;

namespace Shopping.Client;

/// <summary>
/// Login through the server's challenge (decision D-19): the password never leaves this process. Keeps the tokens;
/// the refresh token is the <see cref="Subject.Credential"/> that takes the session back after a reload.
/// </summary>
public sealed class RemoteAuthentication : IAuthentication
{
    private readonly HttpClient _http;
    private readonly RestClient _rest;
    private readonly ShoppingClient.Credentials _credentials;
    private readonly SemaphoreSlim _refreshing = new(1, 1);

    internal RemoteAuthentication(HttpClient http, RestClient rest, ShoppingClient.Credentials credentials)
    {
        _http = http;
        _rest = rest;
        _credentials = credentials;
    }

    public async Task<Subject?> Login(string userName, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        userName = userName.Trim();
        var challenge = await _http.GetAsync($"/api/auth/challenge?userName={Uri.EscapeDataString(userName)}", cancellationToken);
        var fields = await Fields(challenge, cancellationToken);
        var derived = PasswordHash.Derive(password, Convert.FromBase64String(fields["salt"].GetString()!), fields["iterations"].GetInt32());
        var nonce = fields["nonce"].GetString()!;
        var digest = PasswordHash.Digest(derived, userName, nonce);

        var answer = await Post("/api/auth/login", w =>
        {
            w.WriteString("userName", userName);
            w.WriteString("nonce", nonce);
            w.WriteString("digest", digest);
        }, cancellationToken);
        return answer.StatusCode == HttpStatusCode.Unauthorized ? null : await Open(answer, cancellationToken);
    }

    public async Task<Subject?> Resume(Subject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Credential is not { } refresh)
        {
            return null;
        }

        var answer = await Post("/api/auth/refresh", w => w.WriteString("refreshToken", refresh), cancellationToken);
        return answer.StatusCode == HttpStatusCode.Unauthorized ? null : await Open(answer, cancellationToken);
    }

    public async Task Logout(Subject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var refresh = _credentials.RefreshToken ?? subject.Credential;
        _credentials.AccessToken = null;
        _credentials.RefreshToken = null;
        if (refresh is not null)
        {
            // The token goes along, so the server revokes it (the Kotlin client sent "{}": bug 20 of its report).
            using var answer = await Post("/api/auth/logout", w => w.WriteString("refreshToken", refresh), cancellationToken);
        }
    }

    /// <summary>After a 401: spends the refresh token for new ones. Whether to repeat the call.</summary>
    internal async Task<bool> RefreshAccess(CancellationToken cancellationToken)
    {
        await _refreshing.WaitAsync(cancellationToken);
        try
        {
            if (_credentials.RefreshToken is not { } refresh)
            {
                return false;
            }

            using var answer = await Post("/api/auth/refresh", w => w.WriteString("refreshToken", refresh), cancellationToken);
            return answer.IsSuccessStatusCode && await Open(answer, cancellationToken) is not null;
        }
        finally
        {
            _refreshing.Release();
        }
    }

    private async Task<Subject?> Open(HttpResponseMessage answer, CancellationToken cancellationToken)
    {
        using (answer)
        {
            var fields = await Fields(answer, cancellationToken);
            _credentials.AccessToken = fields["accessToken"].GetString();
            _credentials.RefreshToken = fields["refreshToken"].GetString();
            return new Subject(fields["userId"].GetInt64(), fields["name"].GetString() ?? string.Empty, _credentials.RefreshToken,
                fields.TryGetValue("historyKey", out var key) ? key.GetString() : null);
        }
    }

    private async Task<HttpResponseMessage> Post(string path, Action<Utf8JsonWriter> fields, CancellationToken cancellationToken)
    {
        var body = RestJson.Write(w =>
        {
            w.WriteStartObject();
            fields(w);
            w.WriteEndObject();
        });
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Add(RestNames.ClientHeader, _rest.ClientId);
        return await _http.SendAsync(request, cancellationToken);
    }

    private static async Task<Dictionary<string, JsonElement>> Fields(HttpResponseMessage answer, CancellationToken cancellationToken)
    {
        var bytes = await answer.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!answer.IsSuccessStatusCode)
        {
            throw RestErrors.FromResponse(answer.StatusCode, RestErrors.MessageOf(bytes));
        }

        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }
}
