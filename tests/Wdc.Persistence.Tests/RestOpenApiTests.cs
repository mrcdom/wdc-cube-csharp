using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Wdc.Domain;
using Wdc.Domain.Tests;
using Wdc.Rest.Server;
using Wdc.Rest.Testing;

namespace Wdc.Persistence.Tests;

/// <summary>The OpenAPI document of the repository and transaction routes, as a host serves it.</summary>
public sealed partial class RestOpenApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] s_readRoutes = ["count", "fetch", "fetch-page", "fetch-by-id"];

    private static readonly string[] s_writeRoutes = ["insert", "update", "delete"];

    [Fact]
    public async Task Lists_every_route_at_the_path_it_is_served()
    {
        using var document = await Document();
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in s_readRoutes.Concat(s_writeRoutes))
        {
            Assert.True(paths.GetProperty($"/api/repo/product/{route}").TryGetProperty("post", out _), route);
        }

        var get = paths.GetProperty("/api/repo/product/{id}").GetProperty("get");
        var id = get.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("in").GetString() == "path");
        Assert.Equal("id", id.GetProperty("name").GetString());
        Assert.Equal("int64", id.GetProperty("schema").GetProperty("format").GetString());

        // A group's prefix is part of the path; a read-only repository has no write routes.
        foreach (var route in s_readRoutes)
        {
            Assert.True(paths.TryGetProperty($"/v2/api/repo/user/{route}", out _), route);
        }

        foreach (var route in s_writeRoutes)
        {
            Assert.False(paths.TryGetProperty($"/v2/api/repo/user/{route}", out _), route);
        }

        Assert.True(paths.GetProperty("/api/tx/begin").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/tx/commit").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/tx/rollback").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/tx/status").TryGetProperty("get", out _));

        // The host's own endpoints stay, and an excluded repository stays out.
        Assert.True(paths.TryGetProperty("/api/hello", out _));
        Assert.DoesNotContain(paths.EnumerateObject(), p => p.Name.Contains("purchase-item", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reads_back_without_errors_and_every_reference_resolves()
    {
        var json = await DocumentText();

        var (document, diagnostic) = OpenApiDocument.Parse(json, "json");
        Assert.NotNull(document);
        Assert.Empty(diagnostic?.Errors ?? []);
        Assert.Empty(document.Validate(ValidationRuleSet.GetDefaultRuleSet()) ?? []);

        using var parsed = JsonDocument.Parse(json);
        var schemas = parsed.RootElement.GetProperty("components").GetProperty("schemas");
        var references = SchemaReference().Matches(json).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(references);
        Assert.All(references, name => Assert.True(schemas.TryGetProperty(name, out _), name));

        var operationIds = parsed.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject())
            .Where(o => o.Value.TryGetProperty("operationId", out _))
            .Select(o => o.Value.GetProperty("operationId").GetString())
            .ToList();
        Assert.Equal(operationIds.Count, operationIds.Distinct().Count());
        Assert.Contains("productFetchPage", operationIds);
        Assert.Contains("transactionCommit", operationIds);
    }

    [Fact]
    public async Task Describes_the_entity_from_its_model()
    {
        using var document = await Document();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var product = schemas.GetProperty("Product").GetProperty("properties");

        Assert.Equal(Product.Model.Fields.Select(f => f.Name), product.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["integer", "null"], Types(product.GetProperty("id")));
        Assert.Equal("int64", product.GetProperty("id").GetProperty("format").GetString());
        Assert.Equal("decimal", product.GetProperty("price").GetProperty("format").GetString());
        Assert.Equal("byte", product.GetProperty("image").GetProperty("format").GetString());
        Assert.Equal(["array", "null"], Types(product.GetProperty("items")));
        Assert.Equal("#/components/schemas/PurchaseItem", product.GetProperty("items").GetProperty("items").GetProperty("$ref").GetString());

        // A reference is the child entity or null; an enum travels by its names.
        var purchase = schemas.GetProperty("Purchase").GetProperty("properties");
        Assert.Equal("#/components/schemas/User", purchase.GetProperty("user").GetProperty("anyOf")[0].GetProperty("$ref").GetString());
        var role = schemas.GetProperty("User").GetProperty("properties").GetProperty("role").GetProperty("anyOf")[0];
        Assert.Equal(Enum.GetNames<Role>(), role.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Describes_the_criteria_the_projection_and_the_answers()
    {
        using var document = await Document();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var criteria = schemas.GetProperty("ProductCriteria").GetProperty("properties");
        var names = new ProductCriteria().Criterions.Select(c => c.Name).Append(CriteriaCodec.OrderByProperty);
        Assert.Equal(names, criteria.EnumerateObject().Select(p => p.Name));
        Assert.Equal("#/components/schemas/Int64Criterion",
            criteria.GetProperty("productId").GetProperty("oneOf")[0].GetProperty("$ref").GetString());
        Assert.Equal(new ProductCriteria().AcceptedOrderBy,
            schemas.GetProperty("ProductOrderBy").GetProperty("anyOf")[0].GetProperty("enum").EnumerateArray().Select(e => e.GetString()));

        // LIKE and ILIKE only on text.
        Assert.Contains("LIKE", Operators(schemas, "StringCriterion"));
        Assert.DoesNotContain("LIKE", Operators(schemas, "Int64Criterion"));
        Assert.Contains("BETWEEN", Operators(schemas, "Int64Criterion"));

        // A collection in a projection: one shape, or the envelope whose where is the child's criteria.
        var items = schemas.GetProperty("ProductProjection").GetProperty("properties").GetProperty("items").GetProperty("oneOf");
        Assert.Equal("#/components/schemas/PurchaseItemProjection", items[0].GetProperty("items").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/PurchaseItemCriteria",
            items[1].GetProperty("properties").GetProperty("where").GetProperty("$ref").GetString());

        var query = schemas.GetProperty("ProductQuery").GetProperty("allOf");
        Assert.Equal("#/components/schemas/ProductCriteria", query[0].GetProperty("$ref").GetString());
        Assert.True(query[1].GetProperty("properties").TryGetProperty("pageSize", out _));

        var page = schemas.GetProperty("ProductPage");
        Assert.Equal(["items", "totalItems"], page.GetProperty("required").EnumerateArray().Select(e => e.GetString()));

        var fetchPage = document.RootElement.GetProperty("paths").GetProperty("/api/repo/product/fetch-page").GetProperty("post");
        Assert.Equal("#/components/schemas/ProductQuery", BodySchema(fetchPage.GetProperty("requestBody")));
        Assert.Equal("#/components/schemas/ProductPage", BodySchema(fetchPage.GetProperty("responses").GetProperty("200")));
        Assert.Equal("#/components/schemas/CubeError", BodySchema(fetchPage.GetProperty("responses").GetProperty("400")));
        Assert.Contains(fetchPage.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "X-Tx-Id");
    }

    [Fact]
    public async Task Without_the_transformer_the_routes_stay_out()
    {
        await using var host = await RestTestHost.Start(b =>
        {
            b.Services.AddCubeRest(remoteTransactions: false);
            b.Services.AddOpenApi();
        }, app =>
        {
            new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model, () => new ProductCriteria(),
                _ => throw new InvalidOperationException()).Map(app);
            app.MapOpenApi();
        }, Ct);
        var response = await host.NewHttpClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("/api/repo/product", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    // :: Helpers

    private static async Task<JsonDocument> Document() => JsonDocument.Parse(await DocumentText());

    private static async Task<string> DocumentText()
    {
        await using var host = await RestTestHost.Start(b =>
        {
            b.Services.AddCubeRest(remoteTransactions: false);
            b.Services.AddOpenApi(o => o.AddCubeRest());
        }, app =>
        {
            // Only the description is read: no repository is ever asked for.
            new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model, () => new ProductCriteria(),
                _ => throw new InvalidOperationException()).Map(app);
            new RepositoryRpc<Purchase, PurchaseCriteria, long>("/api/repo/purchase", Purchase.Model, () => new PurchaseCriteria(),
                _ => throw new InvalidOperationException()).Map(app);
            var users = new RepositoryRpc<User, UserCriteria, long>("/api/repo/user", User.Model, () => new UserCriteria(),
                _ => throw new InvalidOperationException());
            users.Writes = false;
            users.Map(app.MapGroup("/v2"));
            new RepositoryRpc<PurchaseItem, PurchaseItemCriteria, long>("/api/repo/purchase-item", PurchaseItem.Model,
                () => new PurchaseItemCriteria(), _ => throw new InvalidOperationException()).Map(app).ExcludeFromDescription();
            app.MapRemoteTransactions();
            app.MapGet("/api/hello", () => "hello");
            app.MapOpenApi();
        }, Ct);
        var response = await host.NewHttpClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static IEnumerable<string?> Types(JsonElement schema) =>
        schema.GetProperty("type").EnumerateArray().Select(t => t.GetString()).Order(StringComparer.Ordinal);

    private static IEnumerable<string?> Operators(JsonElement schemas, string criterion) =>
        schemas.GetProperty(criterion).GetProperty("properties").GetProperty("p").GetProperty("items")
            .GetProperty("properties").GetProperty("o").GetProperty("enum").EnumerateArray().Select(e => e.GetString());

    private static string? BodySchema(JsonElement withContent) =>
        withContent.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();

    [GeneratedRegex("\"#/components/schemas/([^\"]+)\"")]
    private static partial Regex SchemaReference();
}
