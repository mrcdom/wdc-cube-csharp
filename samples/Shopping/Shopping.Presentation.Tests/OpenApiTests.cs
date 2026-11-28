using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shopping.Persistence;
using Shopping.Server;
using Wdc.Domain;

namespace Shopping.Presentation.Tests;

/// <summary>The API's description and its page: served in development, absent elsewhere. No database is reached.</summary>
public sealed class OpenApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<WebApplication> Serve(ShoppingStore store, string environment)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddShoppingApi(store, new ShoppingAuthOptions { AuthRequestsPerMinute = 0 });
        builder.Services.AddShoppingOpenApi();
        var app = builder.Build();
        app.MapShoppingApi();
        app.MapShoppingOpenApi();
        await app.StartAsync(Ct);
        return app;
    }

    // Nothing listens on port 1: describing the API never opens a connection.
    private static ShoppingStore NoDatabase() =>
        ShoppingStore.Open(DatabaseProvider.PostgreSql, "Host=127.0.0.1;Port=1;Timeout=2", "nowhere");

    [Fact]
    public async Task InDevelopment_DescribesTheRepositories_WithTheBearerToken()
    {
        await using var store = NoDatabase();
        await using var app = await Serve(store, Environments.Development);
        var http = app.GetTestClient();

        var response = await http.GetAsync("/openapi/v1.json", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = document.RootElement;
        Assert.Equal("Shopping API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("bearer", root.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty(ShoppingOpenApi.BearerScheme).GetProperty("scheme").GetString());

        var paths = root.GetProperty("paths");
        foreach (var entity in new[] { "user", "product", "purchase", "purchase-item", "purchase-amendment", "purchase-amendment-line" })
        {
            var fetchPage = paths.GetProperty($"/api/repo/{entity}/fetch-page").GetProperty("post");
            Assert.True(fetchPage.TryGetProperty("security", out _), entity);
        }

        Assert.True(paths.TryGetProperty("/api/tx/begin", out _));
        var image = paths.GetProperty("/api/repo/product/{productId}/image").GetProperty("get");
        Assert.False(image.TryGetProperty("security", out _));

        var page = await http.GetAsync("/scalar/", Ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OutsideDevelopment_NeitherTheDescriptionNorItsPage()
    {
        await using var store = NoDatabase();
        await using var app = await Serve(store, Environments.Production);
        var http = app.GetTestClient();

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/openapi/v1.json", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/scalar/", Ct)).StatusCode);
    }
}
