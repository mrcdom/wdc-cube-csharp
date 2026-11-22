using System.Net;
using Shopping.Client;
using Wdc.Rest.Client;

namespace Shopping.Presentation.Tests;

/// <summary>The image answers of the server, as the remote repository reads them (no database needed).</summary>
public sealed class RemoteImageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProductWithNoImage_IsNull()
    {
        var products = Answering(HttpStatusCode.NoContent);

        Assert.Null(await products.FetchImage(7, Ct));
    }

    [Fact]
    public async Task AProductWithAnImage_IsItsBytes()
    {
        var products = Answering(HttpStatusCode.OK, [0x89, (byte)'P', (byte)'N', (byte)'G']);

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], await products.FetchImage(7, Ct));
    }

    [Fact]
    public async Task AnUnknownProduct_IsNotFound()
    {
        var products = Answering(HttpStatusCode.NotFound, """{"error":"No product 7."}"""u8.ToArray());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => products.FetchImage(7, Ct));
    }

    private static RemoteProductRepository Answering(HttpStatusCode status, byte[]? body = null) =>
        new(new RestClient(new HttpClient(new Fixed(status, body)) { BaseAddress = new Uri("http://server.test/") }));

    private sealed class Fixed(HttpStatusCode status, byte[]? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body ?? []) });
    }
}
