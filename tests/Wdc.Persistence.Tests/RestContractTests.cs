using System.Net;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Time.Testing;
using Wdc.Domain;
using Wdc.Domain.Tests;
using Wdc.Rest;
using Wdc.Rest.Client;
using Wdc.Rest.Server;

namespace Wdc.Persistence.Tests;

/// <summary>
/// What the REST layer adds to the repository contract (decisions D-17 and D-18): the wire of the Kotlin version, the
/// errors, the limits, the caller's reach, and remote transactions. On PostgreSQL.
/// </summary>
public sealed class RestContractTests(PostgreSqlFixture fixture) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private RestTestHost? _host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Shopping Local => fixture.Shopping!;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(fixture.Shopping is null, fixture.SkipReason);
        await fixture.Reset();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    private async Task<RestTestHost> Host(Action<RestServerOptions>? configure = null, Action<RemoteTransactionOptions>? transactions = null,
        Action<RepositoryRpc<Purchase, PurchaseCriteria, long>>? purchases = null, TimeProvider? time = null)
    {
        _host = await RestTestHost.Start(Local, configure, transactions, configurePurchases: purchases, time: time);
        return _host;
    }

    private static async Task<(HttpStatusCode Status, string Body)> Raw(RestTestHost host, HttpMethod method, string path,
        string? json = null, params (string Name, string Value)[] headers)
    {
        using var http = host.App.GetTestClient();
        using var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        using var response = await http.SendAsync(request, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<Product> NewProduct(string name, decimal price)
    {
        var product = new Product { Name = name, Price = price };
        await Local.Products.Insert(product, cancellationToken: Ct);
        return product;
    }

    private async Task<(User Ana, User Bia, Purchase OfAna, Purchase OfBia)> TwoBuyers()
    {
        var ana = new User { UserName = "ana", Name = "Ana", Password = "x" };
        var bia = new User { UserName = "bia", Name = "Bia", Password = "x" };
        await Local.Users.Insert(ana, cancellationToken: Ct);
        await Local.Users.Insert(bia, cancellationToken: Ct);
        var ofAna = new Purchase { User = ana };
        var ofBia = new Purchase { User = bia };
        await Local.Purchases.Insert(ofAna, cancellationToken: Ct);
        await Local.Purchases.Insert(ofBia, cancellationToken: Ct);
        return (ana, bia, ofAna, ofBia);
    }

    // :: The wire of the Kotlin version

    [Fact]
    public async Task Wire_FetchWithProjection_AnswersTheKotlinShape()
    {
        var ball = await NewProduct("Bola de futebol", 10m);
        var host = await Host();

        var (status, body) = await Raw(host, HttpMethod.Post, "/api/repo/product/fetch",
            $$$"""{"productId":{{{ball.Id}}},"projection":{"id":1,"name":"~"}}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal($$"""{"items":[{"id":{{ball.Id}},"name":"Bola de futebol"}]}""", body);
    }

    [Fact]
    public async Task Wire_InsertLooseEntity_UpdatePartial_DeleteByCriteria()
    {
        var host = await Host();

        var (_, inserted) = await Raw(host, HttpMethod.Post, "/api/repo/product/insert", """{"name":"Teclado","price":80.5}""");
        var id = long.Parse(inserted.Split("\"id\":")[1].Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("""{"success":true,"id":""", inserted, StringComparison.Ordinal);

        Assert.Equal((HttpStatusCode.OK, """{"success":true}"""),
            await Raw(host, HttpMethod.Post, "/api/repo/product/update", $$"""{"id":{{id}},"name":"Teclado 2"}"""));
        Assert.Equal("Teclado 2", (await Local.Products.FetchById(id, cancellationToken: Ct))!.Name);
        Assert.Equal(80.5m, (await Local.Products.FetchById(id, cancellationToken: Ct))!.Price); // absent: untouched

        Assert.Equal((HttpStatusCode.OK, """{"count":1}"""),
            await Raw(host, HttpMethod.Post, "/api/repo/product/delete", $$$"""{"productId":{{{id}}}}"""));
    }

    [Fact]
    public async Task Wire_UnknownOperatorIsDropped_UnknownOrderingIs400()
    {
        await NewProduct("a", 1m);
        await NewProduct("b", 2m);
        var host = await Host();

        var (_, body) = await Raw(host, HttpMethod.Post, "/api/repo/product/count", """{"name":{"p":[{"o":"REGEXP","v":["x"]}]}}""");
        Assert.Equal("""{"count":2}""", body); // looser, never different

        var (status, error) = await Raw(host, HttpMethod.Post, "/api/repo/product/fetch", """{"orderBy":"BY_MOOD"}""");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("CHEAPEST_FIRST", error, StringComparison.Ordinal);
        Assert.StartsWith("""{"error":""", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_MalformedBody400_UnknownId404_EmptyDelete400()
    {
        var host = await Host();

        Assert.Equal(HttpStatusCode.BadRequest, (await Raw(host, HttpMethod.Post, "/api/repo/product/fetch", "{not json")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Raw(host, HttpMethod.Get, "/api/repo/product/987654")).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Raw(host, HttpMethod.Post, "/api/repo/product/fetch-by-id", """{"id":987654}""")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Raw(host, HttpMethod.Post, "/api/repo/product/delete", "{}")).Status);

        // And on the client side, the exceptions of the contract.
        var remote = new RemoteShopping(host.NewClient());
        Assert.Null(await remote.Products.FetchById(987654, cancellationToken: Ct));
        await Assert.ThrowsAsync<InvalidRequestException>(() => remote.Products.FetchPage(new ProductCriteria(), 0, 1_000_000, Ct)); // 400
    }

    [Fact]
    public async Task Limits_TooManyItemsAreRefused_NotSilentlyCut()
    {
        for (var i = 0; i < 3; i++)
        {
            await NewProduct($"p{i}", i);
        }

        var host = await Host(o => o.MaxItems = 2);
        var remote = new RemoteShopping(host.NewClient());

        await Assert.ThrowsAsync<InvalidRequestException>(() => remote.Products.Fetch(new ProductCriteria(), cancellationToken: Ct));
        await Assert.ThrowsAsync<InvalidRequestException>(() => remote.Products.Fetch(new ProductCriteria(), limit: 3, cancellationToken: Ct));
        await Assert.ThrowsAsync<InvalidRequestException>(() => remote.Products.FetchPage(new ProductCriteria(), 0, 3, Ct));
        Assert.Equal(2, (await remote.Products.Fetch(new ProductCriteria(), limit: 2, cancellationToken: Ct)).Count);
    }

    // :: The caller's reach (bug 13 of the Kotlin report)

    private static void ScopeByTestUser(RepositoryRpc<Purchase, PurchaseCriteria, long> rpc)
    {
        rpc.RestrictToId = (c, id) => c.PurchaseId.Restrict(id);
        rpc.Scope = (context, c) =>
            c.UserId.Restrict(long.Parse(context.Request.Headers["X-Test-User"].ToString(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Scope_ADisjunctiveRequestOnTheScopedField_DoesNotWidenIt()
    {
        var (ana, _, ofAna, _) = await TwoBuyers();
        var host = await Host(purchases: ScopeByTestUser);
        var asAna = ("X-Test-User", ana.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var (_, body) = await Raw(host, HttpMethod.Post, "/api/repo/purchase/fetch",
            """{"userId":{"or":true,"p":[{"o":"IS_NOT_NULL"}]},"projection":{"id":1}}""", asAna);

        Assert.Equal($$$"""{"items":[{"id":{{{ofAna.Id}}}}]}""", body);
    }

    [Fact]
    public async Task Scope_GuardsFetchByIdUpdateAndDelete()
    {
        var (ana, _, _, ofBia) = await TwoBuyers();
        var host = await Host(purchases: ScopeByTestUser);
        var asAna = ("X-Test-User", ana.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(HttpStatusCode.NotFound,
            (await Raw(host, HttpMethod.Post, "/api/repo/purchase/fetch-by-id", $$$"""{"id":{{{ofBia.Id}}}}""", asAna)).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Raw(host, HttpMethod.Get, $"/api/repo/purchase/{ofBia.Id}", null, asAna)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Raw(host, HttpMethod.Post, "/api/repo/purchase/update",
            $$"""{"id":{{ofBia.Id}},"buyDate":"2020-01-01T00:00:00Z"}""", asAna)).Status);
        Assert.Equal((HttpStatusCode.OK, """{"count":0}"""),
            await Raw(host, HttpMethod.Post, "/api/repo/purchase/delete", $$$"""{"purchaseId":{{{ofBia.Id}}}}""", asAna));
        Assert.Equal(HttpStatusCode.BadRequest, // the reach alone is not a filter: "delete all mine" is refused
            (await Raw(host, HttpMethod.Post, "/api/repo/purchase/delete", "{}", asAna)).Status);

        Assert.NotNull(await Local.Purchases.FetchById(ofBia.Id!.Value, cancellationToken: Ct));
    }

    // :: Remote transactions

    [Fact]
    public async Task RemoteTx_ReadsInsideSeeTheirOwnWrites_AndCommitPublishesThem()
    {
        var host = await Host();
        var remote = new RemoteShopping(host.NewClient());

        await remote.Tx.Required(async _ =>
        {
            await remote.Products.Insert(new Product { Name = "pending", Price = 1m }, cancellationToken: Ct);
            Assert.Equal(1, await remote.Products.Count(new ProductCriteria().Name.Eq("pending"), Ct)); // participates
            Assert.Equal(0, await Local.Products.Count(new ProductCriteria().Name.Eq("pending"), Ct)); // not yet outside
        }, Ct);

        Assert.Equal(1, await Local.Products.Count(new ProductCriteria().Name.Eq("pending"), Ct));
        Assert.Equal(0, host.Coordinator.Stats.OpenNow);
    }

    [Fact]
    public async Task RemoteTx_ARequestThatFailedInside_PreventsTheCommit()
    {
        var host = await Host();
        var remote = new RemoteShopping(host.NewClient());

        var caught = await Assert.ThrowsAsync<TransactionSystemException>(() => remote.Tx.Required(async _ =>
        {
            await remote.Products.Insert(new Product { Name = "half", Price = 1m }, cancellationToken: Ct);
            try
            {
                await remote.Products.Insert(new Product { Name = null, Price = 1m }, cancellationToken: Ct); // NOT NULL: fails
            }
            catch (RemoteCallException)
            {
                // the caller swallows the failure and goes on to commit
            }
        }, Ct));

        Assert.Contains("rolled back", caught.Message, StringComparison.Ordinal);
        Assert.Equal(0, await Local.Products.Count(new ProductCriteria().Name.Eq("half"), Ct));
    }

    [Fact]
    public async Task RemoteTx_AWriteWithoutTheHeader_IsRefusedForTheSameClientOnly()
    {
        var host = await Host();
        var client = host.NewClient(new RestClientOptions { ClientId = "phone" });
        var remote = new RemoteShopping(client);
        var otherDevice = new RemoteShopping(host.NewClient(new RestClientOptions { ClientId = "laptop" }));

        await remote.Tx.Required(async _ =>
        {
            var lostHeader = await Raw(host, HttpMethod.Post, "/api/repo/product/insert", """{"name":"x","price":1}""",
                (RestNames.ClientHeader, "phone"));
            Assert.Equal(HttpStatusCode.Conflict, lostHeader.Status);

            // Another client of the same user writes freely (bug 18 of the Kotlin report).
            await otherDevice.Tx.NotSupported(_ => otherDevice.Products.Insert(new Product { Name = "y", Price = 1m }, cancellationToken: Ct), Ct);
        }, Ct);

        Assert.Equal(1, await Local.Products.Count(new ProductCriteria().Name.Eq("y"), Ct));
    }

    [Fact]
    public async Task RemoteTx_Outcomes_AreIdempotent_AndUnknownIs409NotServerError()
    {
        var host = await Host();
        var client = ("X-Client-Id", "c1");
        var (_, begun) = await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", client);
        var id = begun.Split('"')[3];
        var tx = (RestNames.TransactionHeader, id);

        Assert.Equal((HttpStatusCode.OK, """{"status":"committed"}"""), await Raw(host, HttpMethod.Post, "/api/tx/commit", "{}", client, tx));
        Assert.Equal((HttpStatusCode.OK, """{"status":"committed"}"""), await Raw(host, HttpMethod.Post, "/api/tx/commit", "{}", client, tx));
        Assert.Equal(HttpStatusCode.Conflict, (await Raw(host, HttpMethod.Post, "/api/tx/rollback", "{}", client, tx)).Status);
        Assert.Equal((HttpStatusCode.OK, """{"status":"committed"}"""), await Raw(host, HttpMethod.Get, "/api/tx/status", null, client, tx));

        var ghost = (RestNames.TransactionHeader, "no-such-tx");
        Assert.Equal(HttpStatusCode.Conflict, (await Raw(host, HttpMethod.Post, "/api/tx/commit", "{}", client, ghost)).Status);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Raw(host, HttpMethod.Post, "/api/repo/product/count", "{}", client, ghost)).Status);
        Assert.Equal((HttpStatusCode.OK, """{"status":"unknown"}"""), await Raw(host, HttpMethod.Get, "/api/tx/status", null, client, ghost));
    }

    [Fact]
    public async Task RemoteTx_BelongsToItsOwner_AndIsLimitedPerOwner()
    {
        var host = await Host(transactions: o => o.MaxOpenPerOwner = 2);
        var (_, begun) = await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", ("X-Client-Id", "c1"));
        var tx = (RestNames.TransactionHeader, begun.Split('"')[3]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Raw(host, HttpMethod.Post, "/api/repo/product/count", "{}", ("X-Client-Id", "c2"), tx)).Status);

        Assert.Equal(HttpStatusCode.OK, (await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", ("X-Client-Id", "c1"))).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", ("X-Client-Id", "c1"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", ("X-Client-Id", "c2"))).Status);
    }

    [Fact]
    public async Task RemoteTx_Idle_IsRolledBackByTheReaper()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = await Host(transactions: o => o.IdleTimeout = TimeSpan.FromSeconds(60), time: time);
        var client = ("X-Client-Id", "c1");
        var (_, begun) = await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", client);
        var tx = (RestNames.TransactionHeader, begun.Split('"')[3]);
        await Raw(host, HttpMethod.Post, "/api/repo/product/insert", """{"name":"abandoned","price":1}""", client, tx);

        time.Advance(TimeSpan.FromSeconds(59));
        await host.Coordinator.Reap(Ct);
        Assert.Equal(1, host.Coordinator.Stats.OpenNow);

        time.Advance(TimeSpan.FromSeconds(2));
        await host.Coordinator.Reap(Ct);

        Assert.Equal(0, host.Coordinator.Stats.OpenNow);
        Assert.Equal((HttpStatusCode.OK, """{"status":"rolledback"}"""), await Raw(host, HttpMethod.Get, "/api/tx/status", null, client, tx));
        Assert.Equal(HttpStatusCode.Conflict, (await Raw(host, HttpMethod.Post, "/api/tx/commit", "{}", client, tx)).Status);
        Assert.Equal(0, await Local.Products.Count(new ProductCriteria().Name.Eq("abandoned"), Ct));
    }

    [Fact]
    public async Task RemoteTx_LostCommitAnswer_AsksTheStatus_InsteadOfReportingFailure()
    {
        var host = await Host();
        var handler = new LoseCommitAnswer(host.App.GetTestServer().CreateHandler());
        var client = new RestClient(new HttpClient(handler) { BaseAddress = host.App.GetTestServer().BaseAddress });
        var remote = new RemoteShopping(client);
        var afterCommit = false;

        await remote.Tx.Required(async _ =>
        {
            await remote.Products.Insert(new Product { Name = "kept", Price = 1m }, cancellationToken: Ct);
            await remote.Tx.AfterCommit(() =>
            {
                afterCommit = true;
                return Task.CompletedTask;
            });
        }, Ct);

        Assert.True(handler.Lost);
        Assert.True(afterCommit);
        Assert.Equal(1, await Local.Products.Count(new ProductCriteria().Name.Eq("kept"), Ct));
    }

    /// <summary>Delivers the commit to the server, then loses its answer.</summary>
    private sealed class LoseCommitAnswer(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public bool Lost { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/tx/commit", StringComparison.Ordinal) && !Lost)
            {
                Lost = true;
                response.Dispose();
                throw new HttpRequestException("connection reset");
            }

            return response;
        }
    }
}
