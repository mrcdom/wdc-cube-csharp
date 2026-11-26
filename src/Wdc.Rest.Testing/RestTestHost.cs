using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Wdc.Rest.Client;

namespace Wdc.Rest.Testing;

/// <summary>
/// An ASP.NET host in memory (no socket) for tests of a REST server: the application's own services, middleware and
/// routes, a <see cref="RestClient"/> of it, and raw requests to check the wire.
/// </summary>
public sealed class RestTestHost : IAsyncDisposable
{
    private RestTestHost(WebApplication app) => App = app;

    public WebApplication App { get; }

    public IServiceProvider Services => App.Services;

    /// <summary>
    /// Builds and starts a host: <paramref name="configure"/> registers the services (as <c>Program.cs</c> does) and
    /// <paramref name="map"/> adds the middleware and the routes. Logging starts empty: add providers in
    /// <paramref name="configure"/> to see it.
    /// </summary>
    public static async Task<RestTestHost> Start(Action<WebApplicationBuilder> configure, Action<WebApplication> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(map);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        configure(builder);
        var app = builder.Build();
        try
        {
            map(app);
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return new RestTestHost(app);
    }

    /// <summary>An <see cref="HttpClient"/> of this host.</summary>
    public HttpClient NewHttpClient() => App.GetTestClient();

    /// <summary>A REST client of this host (a new client id each time, unless the options say otherwise).</summary>
    public RestClient NewClient(RestClientOptions? options = null) => new(NewHttpClient(), options);

    /// <summary>A raw request: <paramref name="json"/> as the body, and the headers given.</summary>
    public async Task<RestTestResponse> Send(HttpMethod method, string path, string? json = null,
        IEnumerable<KeyValuePair<string, string>>? headers = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        using var http = NewHttpClient();
        using var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        foreach (var (name, value) in headers ?? [])
        {
            request.Headers.Add(name, value);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Copy(response.Headers, all);
        Copy(response.Content.Headers, all);
        return new RestTestResponse(response.StatusCode, body, all);
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();

    private static void Copy(HttpHeaders from, Dictionary<string, string> to)
    {
        foreach (var (name, values) in from)
        {
            to[name] = string.Join(", ", values);
        }
    }
}
