using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopping.Presentation.Tests;

namespace Shopping.Blazor.Tests;

/// <summary>The real host, in memory: the image endpoint.</summary>
public sealed class ImageEndpointTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    [Fact]
    public async Task ProductImages_200WithPng_404ForAnUnknownProduct()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Shopping:Database:Provider", "PostgreSql");
            host.UseSetting("Shopping:Database:ConnectionString", Environment.GetEnvironmentVariable("WDC_TEST_POSTGRES"));
            host.UseSetting("Shopping:Database:Schema", db.Store!.Database.Schema);
        });
        using var client = factory.CreateClient();
        var products = await db.Store.Repositories.Products.Fetch(new Domain.Products.ProductCriteria(),
            cancellationToken: Xunit.TestContext.Current.CancellationToken);

        var image = await client.GetAsync(new Uri($"/image/product/{products[0].Id}.png", UriKind.Relative),
            Xunit.TestContext.Current.CancellationToken);
        var missing = await client.GetAsync(new Uri("/image/product/999999.png", UriKind.Relative),
            Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // The pages come from the shared views library: the host must route to them, at any address.
        foreach (var address in new[] { "/", "/home/cart", "/_content/Shopping.Blazor.Views/app.css" })
        {
            var page = await client.GetAsync(new Uri(address, UriKind.Relative), Xunit.TestContext.Current.CancellationToken);
            Assert.True(page.StatusCode == HttpStatusCode.OK, $"{address}: {page.StatusCode}");
        }
    }
}
