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
    o => o.MaxItems = 1_000, // largest fetch and pageSize (default 10,000)
    configureTransactions: t => t.IdleTimeout = TimeSpan.FromSeconds(30)); // default 60 s
```

| `RestServerOptions` | Default | Meaning |
|---|---|---|
| `MaxBodyBytes` | 16 MB | Largest request body read. |
| `MaxItems` | 10,000 | Largest `fetch` (an unlimited fetch that matches more is a 400) and largest `pageSize`. |
| `RefuseWritesOutsideOpenTransaction` | `true` | A write without `X-Tx-Id` while its owner has a transaction open is a 409. |
| `OwnerOf` | `RestServerOptions.DefaultOwner` | Who owns a remote transaction: `user:<id>\|client:<X-Client-Id>`. |

| `RemoteTransactionOptions` | Default |
|---|---|
| `MaxOpen` / `MaxOpenPerOwner` | 128 / 16 (beyond them, `begin` answers 429) |
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
| `TransactionConflictException` | 409 | `TransactionConflictException` |
| `TransactionLimitExceededException` | 429 | `TransactionLimitExceededException` |
| anything else | 500, `"Internal server error"` | `RemoteCallException` (with `Status`) |

- A 500 never sends the server's exception message; the server logs it (`Wdc.Rest.Server` category).
- A database error (a `NOT NULL` violated, a foreign key) is "anything else". To give the user a 400 with a message,
  validate in the repository and throw `InvalidRequestException`, as `PurchaseItemRepository.Insert` does.
- A proxy page or any status outside the table also becomes `RemoteCallException`.

## Checklist

- [ ] `ITransactionService` and `IDetachedTransactionSource` are registered before `AddCubeRest`.
- [ ] The repository function of each `RepositoryRpc` returns the protected repository of the request.
- [ ] `UseAuthentication` runs before the routes, and unauthenticated `/api` requests get the contract's 401.
- [ ] `MapRemoteTransactions` is mapped when remote transactions are on.
- [ ] Each `RemoteRepository` uses the same base path, model and criteria as its `RepositoryRpc`.
- [ ] The signing key and the connection string come from configuration or a secret store.

## See also

- [Remote](../concepts/remote.md): the wire, the server pipeline, the error table and the remote transaction rules.
- [Wire protocol](../reference/wire-protocol.md): every body, field by field.
- [Transactions](../concepts/transactions.md): propagations, detached and remote transactions.
- [Protected repositories](../concepts/protected-repositories.md) and [Writing roles and rules](roles-and-rules.md).
- [Writing a SQL repository](sql-repositories.md): the repositories served here.
- [Trimming and AOT](trimming-and-aot.md): publishing the WebAssembly and desktop clients.
- [The Shopping sample](../samples/shopping.md)
