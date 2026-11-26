# Exposing repositories over HTTP

This guide puts a server in front of your repositories and connects a client to it: the routes of each repository
with `RepositoryRpc`, per-request and remote transactions with `AddCubeRest` and `MapRemoteTransactions`, a JWT bearer
token that tells each request who is acting, and on the other side `RestClient`, `RemoteRepository` and
`RemoteTransactionService`, which implement the same contracts as the local repositories. A presenter or use case
written against `IRepository` and `ITransactionService` runs unchanged on either side. It is a how-to; for the wire
format, the error table and the transaction rules, read [Remote](../concepts/remote.md).

The examples come from the Shopping sample: the server in `samples/Shopping/Shopping.Server`, the client in
`samples/Shopping/Shopping.Client`. The framework's tests of the wire, limits and remote transactions are in
`tests/Wdc.Persistence.Tests/RestContractTests.cs`; `RestTestHost.cs` next to it is a minimal server.

## Projects and packages

| Project | References |
|---|---|
| Server (ASP.NET Core) | `Wdc.Rest.Server`, your persistence project, `Microsoft.AspNetCore.Authentication.JwtBearer` for tokens |
| Client (WebAssembly, desktop, tests) | `Wdc.Rest.Client`, your domain project |
| Tests of the server | `Wdc.Rest.Testing` (see [section 9](#9-test-the-server)) |

Both sides reference `Wdc.Rest` (through those packages) and the **same domain project**: the entity models and
criteria are what encode and decode the bodies, so the client needs nothing else to talk to the server.

## 1. Register the services

On the server, register the transaction service, the source of detached transactions, and `AddCubeRest`:

```csharp
// samples/Shopping/Shopping.Server/ShoppingApi.cs
services.AddSingleton<ITransactionService>(store.Database);
services.AddSingleton<IDetachedTransactionSource>(store.Database);
services.AddCubeRest();
```

- `ITransactionService` gives each write request its own transaction (`Required`). Without it, writes run without one.
- `IDetachedTransactionSource` (the `Database`) is required by remote transactions: each one holds its own connection.
- `AddCubeRest(configure, remoteTransactions, configureTransactions)` registers `RestServerOptions` and, unless
  `remoteTransactions: false`, the `RemoteTransactionCoordinator` and the `RemoteTransactionReaper` hosted service.

Tune the limits through the two callbacks:

```csharp
services.AddCubeRest(
    o => o.MaxItems = 500, // largest fetch and pageSize (default 1,000)
    configureTransactions: t => t.IdleTimeout = TimeSpan.FromSeconds(30)); // default 60 s
```

| `RestServerOptions` | Default | Meaning |
|---|---|---|
| `MaxBodyBytes` | 16 MB | Largest request body read. |
| `MaxItems` | 1,000 | Largest `fetch` (an unlimited fetch that matches more is a 400) and largest `pageSize`. |
| `RefuseWritesOutsideOpenTransaction` | `true` | A write without `X-Tx-Id` while its owner has a transaction open is a 409. |
| `OwnerOf` | `RestServerOptions.DefaultOwner` | Who owns a remote transaction: `user:<id>\|client:<X-Client-Id>`. |
| `QuotaOf` | `RestServerOptions.DefaultQuota` | Whose cap a new remote transaction counts against: `user:<id>`, or the owner when anonymous, so a new client id doesn't buy a new cap. |
| `SecurityHeaders` | `nosniff`, `X-Frame-Options: DENY` | Headers on every answer, errors included (see [section 8](#8-headers-timing-limits-and-metrics)). |
| `CacheControl` | `no-store` | `Cache-Control` of the answers whose route set none; `null` sends none. |
| `ServerTiming` / `ServerTimingDescriptions` | off / off | Sends `Server-Timing`; with descriptions. |
| `Errors` | the contract's table | Maps the application's exceptions to answers (see [section 7](#7-handle-errors)). |

| `RemoteTransactionOptions` | Default |
|---|---|
| `MaxOpen` / `MaxOpenPerOwner` | 128 / 16 (beyond them, `begin` answers 429); the per-owner cap is the user's, across all its clients |
| `IdleTimeout` / `MaxLifetime` | 60 s / 10 min (then the reaper rolls back) |
| `OutcomeRetention` / `ReapInterval` | 5 min / 5 s |

## 2. Resolve the repositories per request

`RepositoryRpc` does not decide who may do what: the repository it calls does. Register, per request, the
[protected repositories](../concepts/protected-repositories.md) built for the actor the request authenticated:

```csharp
// samples/Shopping/Shopping.Server/ShoppingApi.cs
services.AddHttpContextAccessor();
services.AddScoped(sp => ShoppingProtection.Protect(store.Repositories,
    new ActorSource(sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User is { } user
        ? ShoppingTokens.ActorOf(user)
        : null)));
```

A request with no valid token gets an `ActorSource` with nobody, so every protected operation throws
`AuthenticationException` (401). Never hand the raw repositories to `RepositoryRpc`.

## 3. Map the routes

One `RepositoryRpc` per entity, then the transaction routes:

```csharp
// samples/Shopping/Shopping.Server/ShoppingApi.cs
new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model, () => new ProductCriteria(),
    c => Of(c).Products).Map(app);
new RepositoryRpc<Purchase, PurchaseCriteria, long>("/api/repo/purchase", Purchase.Model, () => new PurchaseCriteria(),
    c => Of(c).Purchases).Map(app);
// ... one per entity

private static ShoppingRepositories Of(HttpContext context) => context.RequestServices.GetRequiredService<ShoppingRepositories>();
```

```csharp
app.MapRemoteTransactions(); // POST /api/tx/begin, commit, rollback; GET /api/tx/status
```

- The arguments are the base path, the entity model, a criteria factory and a function from the `HttpContext` to the
  repository. `Map` returns the `RouteGroupBuilder`, so you can chain endpoint conventions on it.
- Under the base path: `POST count`, `fetch`, `fetch-page`, `fetch-by-id`, `insert`, `update`, `delete`, and
  `GET {id}`.
- For a read-only entity set `Writes = false` before `Map`; the function may then return an `IReadOnlyRepository`:

  ```csharp
  new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model, () => new ProductCriteria(),
      c => Of(c).Products) { Writes = false }.Map(app);
  ```

- A route outside the contract (an image, a report) is an ordinary minimal-API endpoint. To answer with the contract's
  error bodies, wrap it in `RestHttp.Respond(context, async () => …)`: it writes the returned bytes as JSON, a `null`
  as 404, and an exception as its status. The sample's `AuthRoutes` does that.

## 4. Authenticate with JWT

The Shopping server validates bearer tokens with ASP.NET Core's JWT handler and keeps the claims as issued:

```csharp
// samples/Shopping/Shopping.Server/ShoppingApi.cs
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false; // keep "sub" and "role" as issued
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = auth.Issuer,
        ValidAudience = auth.Issuer,
        IssuerSigningKey = auth.SigningKey,
        RoleClaimType = ShoppingTokens.RoleClaim,
        NameClaimType = "name",
        ClockSkew = TimeSpan.FromSeconds(30),
    };
    o.Events = new JwtBearerEvents
    {
        OnChallenge = c =>
        {
            c.HandleResponse();
            return Task.CompletedTask;
        },
    };
});
```

- `MapInboundClaims = false` keeps `sub` as `sub`. `RestServerOptions.DefaultOwner` reads the user from
  `ClaimTypes.NameIdentifier` or `sub`, so either works for the owner of a remote transaction; the sample's
  `ShoppingTokens.ActorOf` reads `sub` and the `role` claims.
- The challenge is silenced so that the 401 body is the contract's `{"error":…}`, written by a small middleware
  instead. It lets through only the login routes and the product images:

```csharp
// samples/Shopping/Shopping.Server/ShoppingApi.cs
public static WebApplication MapShoppingApi(this WebApplication app)
{
    app.UseAuthentication();
    app.Use(RequireAuthentication);
    app.MapShoppingAuth();
    app.MapRemoteTransactions();
    // ... the image routes
    MapRepositories(app);
    return app;
}

private static async Task RequireAuthentication(HttpContext context, RequestDelegate next)
{
    var path = context.Request.Path;
    var open = path.StartsWithSegments("/api/auth")
               || (HttpMethods.IsGet(context.Request.Method) && path.StartsWithSegments("/api/repo/product")
                   && path.Value!.EndsWith("/image", StringComparison.Ordinal));
    if (path.StartsWithSegments("/api") && !open && context.User.Identity?.IsAuthenticated != true)
    {
        await RestHttp.WriteError(context, new AuthenticationException("Authentication required."));
        return;
    }

    await next(context);
}
```

- The signing key is read from configuration (`Shopping:Jwt:Key`, Base64) by `ShoppingApi.AuthOptions`; outside
  Development a missing key stops the server. Keep it in a secret store, not in `appsettings.json`.
- Issuing tokens is the application's business. The sample's `AuthRoutes` and `ShoppingTokens` serve a challenge
  login (`/api/auth/challenge`, `login`, `refresh`, `logout`) that issues a short-lived access token and a refresh
  token, reading users through the raw store since nobody is acting yet.
- No `UseAuthorization` or `[Authorize]` is needed for the repository routes: the policies of the protected
  repositories are the authorization.

## 5. Connect the client

On the client, one `RestClient` over an `HttpClient` whose `BaseAddress` is the server, and one `RemoteRepository`
subclass per entity:

```csharp
// samples/Shopping/Shopping.Client/RemotePurchaseRepository.cs
public sealed class RemotePurchaseRepository(RestClient client)
    : RemoteRepository<Purchase, PurchaseCriteria, long>(client, "/api/repo/purchase", Purchase.Model, () => new PurchaseCriteria()),
        IPurchaseRepository;
```

```csharp
// samples/Shopping/Shopping.Client/ShoppingClient.cs
var options = new RestClientOptions
{
    AccessToken = _ => ValueTask.FromResult(_credentials.AccessToken),
    RefreshAccess = ct => new ValueTask<bool>(Authentication!.RefreshAccess(ct)), // set just below
};
Rest = new RestClient(http, options);
Authentication = new RemoteAuthentication(http, Rest, _credentials);
Repositories = new ShoppingRepositories(
    new RemoteUserRepository(Rest),
    new RemoteProductRepository(Rest),
    new RemotePurchaseRepository(Rest),
    new RemotePurchaseItemRepository(Rest),
    new RemotePurchaseAmendmentRepository(Rest),
    new RemotePurchaseAmendmentLineRepository(Rest),
    new RemoteTransactionService(Rest));
```

- The base path of each `RemoteRepository` must be the server's `RepositoryRpc` base path.
- `AccessToken` returns the bearer token for each call (or `null`). `RefreshAccess` runs after a 401: return `true`
  to repeat the call once with the new token, `false` to fail with `AuthenticationException`.
- `ClientId` (a new GUID by default) is sent as `X-Client-Id`. Keep one `RestClient` per application instance, so its
  remote transactions have one owner.
- Operations the domain interface adds go in the subclass, through `Client.Post`, `Client.PostOrNull`, `Client.Get`
  or `Client.GetOrNull` and the protected `BasePath`. `Get` throws on an error status (404 as `KeyNotFoundException`)
  and returns an empty body for a 204, so "not found" and "nothing" stay apart; `PostOrNull` and `GetOrNull` return
  `null` for a 404 **and** for a 204.

Register what the presentation needs, as the WebAssembly host does:

```csharp
// samples/Shopping/Shopping.Wasm/Program.cs
var client = new ShoppingClient(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(client.Repositories);
builder.Services.AddSingleton<IAuthentication>(client.Authentication);
```

## 6. Use remote transactions from a use case

A use case written against `ITransactionService` needs no change. On the client, `Required` opens a remote
transaction, and every call of the same `RestClient` in that flow carries its `X-Tx-Id`:

```csharp
// tests/Wdc.Persistence.Tests/RestContractTests.cs
await remote.Tx.Required(async _ =>
{
    await remote.Products.Insert(new Product { Name = "pending", Price = 1m }, cancellationToken: Ct);
    Assert.Equal(1, await remote.Products.Count(new ProductCriteria().Name.Eq("pending"), Ct)); // participates
    Assert.Equal(0, await Local.Products.Count(new ProductCriteria().Name.Eq("pending"), Ct)); // not yet outside
}, Ct);
```

- Normal return commits; an exception rolls back and is rethrown unchanged.
- If any request inside failed, even one the code caught, the server has marked the transaction rollback-only and the
  commit throws `TransactionSystemException`: the work did not happen.
- Await the calls one after the other. The server serves the requests of one transaction one at a time, so parallel
  calls gain nothing. Do not leave a call running past the end of the work: it no longer belongs to the transaction.
- Keep remote transactions short: each holds a database connection on the server until it ends or the reaper rolls it
  back.

## 7. Handle errors

The client raises the same exceptions the server threw, so a presenter catches the domain's types and not HTTP
statuses:

| Server throws | Status | Client throws |
|---|---|---|
| `InvalidRequestException` (also `JsonException`, `FormatException`) | 400 | `InvalidRequestException` |
| `AuthenticationException` | 401 | `AuthenticationException` (once `RefreshAccess`, if set, could not renew the token) |
| `AccessDeniedException` | 403 | `AccessDeniedException` |
| `KeyNotFoundException` | 404 | `KeyNotFoundException` (`FetchById` returns `null` instead) |
| `TransactionConflictException` | 409 | `TransactionConflictException` (on a remote commit: `TransactionSystemException`) |
| `TransactionNotFoundException` | 410 | `TransactionNotFoundException` |
| `TooManyRequestsException` (also `TransactionLimitExceededException`) | 429, with `Retry-After` when known | `TooManyRequestsException` with `RetryAfter` (from `begin`: `TransactionLimitExceededException`) |
| anything else | 500, `"Internal server error"` | `RemoteCallException` (with `Status`) |

- A 500 never sends the server's exception message; the server logs it (`Wdc.Rest.Server` category).
- A database error (a `NOT NULL` violated, a foreign key) is "anything else". To give the user a 400 with a message,
  validate in the repository and throw `InvalidRequestException`, as `PurchaseItemRepository.Insert` does.
- A proxy page or any status outside the table also becomes `RemoteCallException`.

Map the application's own exceptions with `RestServerOptions.Errors`. The most specific type is tried first; a map
that returns `null` passes the exception to the map of its base type, and in the end to the table above:

```csharp
services.AddCubeRest(o => o.Errors
    .Map<OutOfStockException>(HttpStatusCode.Conflict)
    .Map<QuotaException>(e => new RestError(HttpStatusCode.PaymentRequired, e.Message)
    {
        Headers = new Dictionary<string, string> { ["X-Quota"] = e.Name },
    }));
```

The client still raises the table's exceptions: a status the table does not have becomes `RemoteCallException`.

## 8. Headers, timing, limits and metrics

Every answer the REST routes write, errors included, carries `RestServerOptions.SecurityHeaders`
(`X-Content-Type-Options: nosniff` and `X-Frame-Options: DENY`, replacing what a route set) and, unless the route set
its own, `Cache-Control: no-store`. Add, remove or change them in the options.

`ServerTiming = true` sends `Server-Timing` with `total` (from the start of the handler to the serialized answer) and
what the handler measures:

```csharp
var rows = await RestTiming.Measure(context, "db", () => repository.Fetch(criteria, cancellationToken: ct), "Fetch");
```

The descriptions (`desc="Fetch"`) go out only with `ServerTimingDescriptions = true`: they may name internals.

Rate limiting, compression and CORS are ASP.NET's own middleware. `RestRateLimiting.OnRejected` makes the rate
limiter answer like the routes: 429, `{"error":"…"}`, `Retry-After` and the headers above. A limit per user and
minute, on the API only:

```csharp
services.AddRateLimiter(o =>
{
    o.OnRejected = RestRateLimiting.OnRejected;
    o.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
});

app.UseRateLimiter(); // after UseAuthentication, so the partition sees the user
var api = app.MapGroup(string.Empty).RequireRateLimiting("api"); // map the REST routes on it
api.MapRemoteTransactions();
```

- A use case can refuse for now itself: `throw new TooManyRequestsException(message, retryAfter)` is a 429 with
  `Retry-After`, and the client raises it with `RetryAfter`.
- With CORS, expose the headers a browser client reads: `.WithExposedHeaders("Retry-After", "Server-Timing")`.
- Compression (`AddResponseCompression`) shrinks large fetches; it is off over HTTPS unless `EnableForHttps`.

The coordinator publishes its numbers on the meter `Wdc.Rest.Server` (`RestServerMetrics.MeterName`):
`wdc.remote.transactions.open` (open now) and `wdc.remote.transactions` by `event` (`begun`, `committed`,
`rolledback`, `reaped`, `refused`). The Blazor host publishes `wdc.cube.applications.live` on `Wdc.Cube.Blazor`.
Read them with `dotnet-counters`, or export them with OpenTelemetry (`AddMeter(RestServerMetrics.MeterName)`).

## 9. Test the server

`Wdc.Rest.Testing` runs the server in memory, with no socket: the same services, middleware and routes as
`Program.cs`, then a `RestClient` of it, or raw requests to check the wire.

```csharp
await using var host = await RestTestHost.Start(
    builder =>
    {
        builder.Services.AddSingleton<ITransactionService>(database);
        builder.Services.AddSingleton<IDetachedTransactionSource>(database);
        builder.Services.AddCubeRest();
    },
    app =>
    {
        new RepositoryRpc<Product, ProductCriteria, long>("/api/repo/product", Product.Model,
            () => new ProductCriteria(), _ => products).Map(app);
        app.MapRemoteTransactions();
    });

// Through the client, as the application calls it
var remote = new RemoteRepository<Product, ProductCriteria, long>(host.NewClient(), "/api/repo/product",
    Product.Model, () => new ProductCriteria());
var cheap = await remote.Fetch(new ProductCriteria().Price.Lt(10m));

// On the wire
var answer = await host.Send(HttpMethod.Post, "/api/repo/product/fetch", """{"orderBy":"BY_MOOD"}""");
Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
Assert.StartsWith("unknown ordering", answer.Error);
Assert.Equal("no-store", answer.Header("Cache-Control"));
```

- Logging starts empty; add a provider in the first callback to see it.
- The test host does not decompress: with compression on, a raw answer shows its `Content-Encoding`.
- Each `NewClient()` is a new client id (`X-Client-Id`), so two clients are two owners of remote transactions.

## Checklist

- [ ] `ITransactionService` and `IDetachedTransactionSource` are registered before `AddCubeRest`.
- [ ] The repository function of each `RepositoryRpc` returns the protected repository of the request.
- [ ] `UseAuthentication` runs before the routes, and unauthenticated `/api` requests get the contract's 401.
- [ ] `MapRemoteTransactions` is mapped when remote transactions are on.
- [ ] Each `RemoteRepository` uses the same base path, model and criteria as its `RepositoryRpc`.
- [ ] The signing key and the connection string come from configuration or a secret store.
- [ ] The rate limiter, if any, answers with `RestRateLimiting.OnRejected`.

## See also

- [Remote](../concepts/remote.md): the wire, the server pipeline, the error table and the remote transaction rules.
- [Wire protocol](../reference/wire-protocol.md): every body, field by field.
- [Transactions](../concepts/transactions.md): propagations, detached and remote transactions.
- [Protected repositories](../concepts/protected-repositories.md) and [Writing roles and rules](roles-and-rules.md).
- [Writing a SQL repository](sql-repositories.md): the repositories served here.
- [Trimming and AOT](trimming-and-aot.md): publishing the WebAssembly and desktop clients.
- [The Shopping sample](../samples/shopping.md)
