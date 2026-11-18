namespace Shopping.Presentation.Tests;

/// <summary>The images on PostgreSQL.</summary>
public sealed class ImageTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    [Fact]
    public async Task ProductImages_AreStoredAndRead_AndAMissingProductSaysSo()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var products = await db.Store!.Repositories.Products.Fetch(new Domain.Products.ProductCriteria(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, products.Count);
        foreach (var product in products)
        {
            var image = await db.Store.Repositories.Products.FetchImage(product.Id!.Value, TestContext.Current.CancellationToken);
            Assert.True(image is { Length: > 1000 } && image[1] == (byte)'P' && image[2] == (byte)'N' && image[3] == (byte)'G');
        }

        await Assert.ThrowsAsync<KeyNotFoundException>(() => db.Store.Repositories.Products.FetchImage(999_999, TestContext.Current.CancellationToken));
    }
}
