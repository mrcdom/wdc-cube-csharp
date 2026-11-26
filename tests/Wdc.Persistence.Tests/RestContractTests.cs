using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Time.Testing;
using Wdc.Domain;
using Wdc.Domain.Tests;
using Wdc.Rest;
using Wdc.Rest.Client;
using Wdc.Rest.Server;

namespace Wdc.Persistence.Tests;

/// <summary>
/// What the REST layer adds to the repository contract: the wire of the Kotlin version, the
/// errors, the limits, the caller's reach, and remote transactions. On PostgreSQL.
/// </summary>
public sealed class RestContractTests(PostgreSqlFixture fixture) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private ShoppingRestHost? _host;

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

    private async Task<ShoppingRestHost> Host(Action<RestServerOptions>? configure = null, Action<RemoteTransactionOptions>? transactions = null,
        Func<HttpContext, IRepository<Purchase, PurchaseCriteria, long>>? purchases = null, TimeProvider? time = null)
    {
        _host = await ShoppingRestHost.Start(Local, configure, transactions, purchases: purchases, time: time);
        return _host;
    }

    private static async Task<(HttpStatusCode Status, string Body)> Raw(ShoppingRestHost host, HttpMethod method, string path,
        string? json = null, params (string Name, string Value)[] headers)
    {
        var response = await host.Host.Send(method, path, json, headers.Select(h => KeyValuePair.Create(h.Name, h.Value)), Ct);
        return (response.Status, response.Body);
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
        var ball = await NewProduct("Soccer ball", 10m);
        var host = await Host();

        var (status, body) = await Raw(host, HttpMethod.Post, "/api/repo/product/fetch",
            $$$"""{"productId":{{{ball.Id}}},"projection":{"id":1,"name":"~"}}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal($$"""{"items":[{"id":{{ball.Id}},"name":"Soccer ball"}]}""", body);
    }

    [Theory]
    [InlineData("""{"price":{"p":[{"o":"GT","v":[null]}]}}""")]
    [InlineData("""{"price":{"p":[{"o":"BETWEEN","v":[1]}]}}""")]
    [InlineData("""{"price":{"p":[{"o":"LIKE","v":[1]}]}}""")]
    [InlineData("""{"price":{"p":[{"o":"EQ","v":["abc"]}]}}""")]
    public async Task Wire_ADeleteWhoseFilterCannotFilter_IsRefused_AndNothingIsDeleted(string criteria)
    {
        await NewProduct("Kept", 1m);
        var host = await Host();
        var before = await Local.Products.Count(new ProductCriteria(), Ct);

        var (status, _) = await Raw(host, HttpMethod.Post, "/api/repo/product/delete", criteria);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(before, await Local.Products.Count(new ProductCriteria(), Ct));
    }

    [Fact]
    public async Task Wire_InsertLooseEntity_UpdatePartial_DeleteByCriteria()
    {
        var host = await Host();

        var (_, inserted) = await Raw(host, HttpMethod.Post, "/api/repo/product/insert", """{"name":"Keyboard","price":80.5}""");
        var id = long.Parse(inserted.Split("\"id\":")[1].Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("""{"success":true,"id":""", inserted, StringComparison.Ordinal);

        Assert.Equal((HttpStatusCode.OK, """{"success":true}"""),
            await Raw(host, HttpMethod.Post, "/api/repo/product/update", $$"""{"id":{{id}},"name":"Keyboard 2"}"""));
        Assert.Equal("Keyboard 2", (await Local.Products.FetchById(id, cancellationToken: Ct))!.Name);
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

    // :: The caller's reach

    /// <summary>The purchases protected for the user named by a test header: each reaches only their own.</summary>
    private IRepository<Purchase, PurchaseCriteria, long> ScopeByTestUser(HttpContext context)
    {
        var policy = new RepositoryPolicy<Purchase, PurchaseCriteria, long>(Purchase.Model, () => new PurchaseCriteria())
        {
            RestrictToId = (c, id) => c.PurchaseId.Restrict(id),
            Scope = (p, c) => c.UserId.Restrict(long.Parse(p.Actor.Id, System.Globalization.CultureInfo.InvariantCulture)),
        };
        var actor = new Actor(context.Request.Headers["X-Test-User"].ToString(), []);
        return new ProtectedRepository<Purchase, PurchaseCriteria, long>(Local.Purchases, policy,
            new Protection(new ActorSource(actor), Local.Database));
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

            // Another client of the same user writes freely.
            await otherDevice.Tx.NotSupported(_ => otherDevice.Products.Insert(new Product { Name = "y", Price = 1m }, cancellationToken: Ct), Ct);
        }, Ct);

        Assert.Equal(1, await Local.Products.Count(new ProductCriteria().Name.Eq("y"), Ct));
    }

    [Fact]
    public async Task RemoteTx_Outcomes_AreIdempotent_AndUnknownIs410NotServerError()
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

        Assert.Equal(HttpStatusCode.Gone, (await Raw(host, HttpMethod.Post, "/api/repo/product/count", "{}", client, tx)).Status);

        var ghost = (RestNames.TransactionHeader, "no-such-tx");
        Assert.Equal(HttpStatusCode.Gone, (await Raw(host, HttpMethod.Post, "/api/tx/commit", "{}", client, ghost)).Status);
        Assert.Equal(HttpStatusCode.Gone,
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
    public async Task RemoteTx_CapIsTheUsers_AcrossItsClients()
    {
        var host = await Host(o => o.QuotaOf = _ => "user:7", transactions: o => o.MaxOpenPerOwner = 2);

        Assert.Equal(HttpStatusCode.OK, (await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", ("X-Client-Id", "c1"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", ("X-Client-Id", "c2"))).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await Raw(host, HttpMethod.Post, "/api/tx/begin", "{}", ("X-Client-Id", "c3"))).Status);
    }

    [Fact]
    public void DefaultQuota_IsTheUser_OrTheAnonymousClient()
    {
        var signedIn = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "7")], "test")),
        };
        signedIn.Request.Headers["X-Client-Id"] = "c1";
        Assert.Equal("user:7", RestServerOptions.DefaultQuota(signedIn));
        Assert.Equal("user:7|client:c1", RestServerOptions.DefaultOwner(signedIn));

        var anonymous = new DefaultHttpContext();
        anonymous.Request.Headers["X-Client-Id"] = "c1";
        Assert.Equal(RestServerOptions.DefaultOwner(anonymous), RestServerOptions.DefaultQuota(anonymous));
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
