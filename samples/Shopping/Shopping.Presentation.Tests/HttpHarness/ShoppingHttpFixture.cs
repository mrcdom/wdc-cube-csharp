using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shopping.Domain;
using Shopping.Presentation.Infra;
using Shopping.Server;

namespace Shopping.Presentation.Tests;

/// <summary>
/// The Shopping server in memory over the same throwaway database: each application instance connects as a
/// new client, through HTTP, with the remote repositories and the JWT login.
/// </summary>
public sealed class ShoppingHttpFixture : ShoppingDatabaseFixture
{
    private WebApplication? _server;

    public WebApplication Server => _server ?? throw new InvalidOperationException("Not started.");

    public override (ShoppingRepositories Repositories, IAuthentication Authentication) Connect()
    {
        var client = new Client.ShoppingClient(Server.GetTestClient());
        return (client.Repositories, client.Authentication);
    }

    /// <summary>A new client of the server.</summary>
    public Client.ShoppingClient NewClient() => new(Server.GetTestClient());

    protected override async Task OnReady()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(TimeProvider.System);
        // Every test client comes from the same address: the per-address limit has its own tests.
        builder.Services.AddShoppingApi(Store!, new Server.ShoppingAuthOptions { AuthRequestsPerMinute = 0 });
        _server = builder.Build();
        _server.MapShoppingApi();
        await _server.StartAsync();
    }

    protected override async Task OnDisposing()
    {
        if (_server is not null)
        {
            await _server.StopAsync();
            await _server.DisposeAsync();
        }
    }
}
