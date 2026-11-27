# Running several instances

One instance of a host needs nothing in this guide. It is for putting several behind one address, for capacity or so
that one can stop while the others serve. It says what the instances must share, what each one keeps in its own
memory, and how requests must be routed so that each one reaches the instance holding what it needs. The Shopping
sample runs this way in a compose setup (two instances of each host, nginx, PostgreSQL, Prometheus), and the CI
checks it on every push.

## What every instance shares

- **The database.** The data, and also what makes a sign-in outlive an instance. In Shopping, these are the
  challenges in flight, the refresh tokens, the failed sign-ins per user name, and the Blazor Server host's
  [Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/introduction) key ring (see
  [Sign-in](../samples/shopping.md#sign-in)). So a challenge issued by one instance is answered on another, a
  refresh token is spent once whichever instance gets it, and failed attempts spread over the instances count
  together.
- **The configuration**, keys included. In Shopping these are `Shopping:Jwt:Key`, which signs the access tokens, and
  `Shopping:History:Key`, from which each user's address key derives. Instances with different keys reject each
  other's tokens and sealed addresses.
- **The same version** of the application: the address format, the JSON on the wire and the scopes a view draws must
  agree.

## What each instance keeps in its own memory

| What | Where | If a request reaches another instance | Route by |
|---|---|---|---|
| A **Blazor Server circuit**: the browser tab's Cube application, its presenters and scopes | The circuit, on the instance that opened it | The circuit does not exist there. The browser opens a new one, and the application starts again from the address. With the key ring shared, the session saved in the browser still reads, so the user stays signed in | Something the browser sends on every request: a cookie |
| A **remote transaction** of the REST API, which holds a database connection | `RemoteTransactionCoordinator` | Calls with its `X-Tx-Id` get 410: the transaction is unknown there | The client's `X-Client-Id`, which `RestClient` sends on every call |
| **Limits per address**, such as ASP.NET's rate limiter | The limiter's partitions | Each instance counts its own requests, so the effective limit is the configured one times the number of instances | Nothing: limit at the proxy instead (below) |

A WebAssembly or desktop client keeps its application in its own process, so only its remote transactions need an
instance. Everything else it asks for can go to any instance.

## Routing

Two rules cover the table:

1. **A browser stays on one Blazor Server instance**, the page and its circuit's connection alike. A load balancer's
   sticky cookie does this. nginx open source has none, so the compose setup's `nginx.conf` hashes on a cookie it sets
   itself: the first answer sets the cookie to the value that request was hashed on, and the next requests hash to the
   same instance. The circuit's connection is a WebSocket: the proxy must pass `Upgrade` and `Connection` through and
   keep idle connections open long enough (`proxy_read_timeout`).
2. **An API client stays on one instance** while it has a remote transaction open: hash on `X-Client-Id`. Requests
   without it, such as the sign-in and the WebAssembly app's files, can go anywhere.

## Health checks

Each Shopping host answers:

- `GET /health/live`: 200 while the process serves requests. Restart the instance if it stops answering.
- `GET /health/ready`: 503 while the database does not answer within 3 seconds. Take the instance out of rotation
  meanwhile.

They are ASP.NET's [health checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks), mapped by
`ShoppingOperations.MapShoppingHealth`. nginx open source never calls them: it takes an instance out only after
connection errors. The compose setup uses `/health/ready` for each container's health and for the start order.

## Metrics

The framework publishes its numbers on `System.Diagnostics.Metrics`, with no dependency:

| Meter | Instruments |
|---|---|
| `Wdc.Rest.Server` | `wdc.remote.transactions.open`, and `wdc.remote.transactions` by `event` (`begun`, `committed`, `rolledback`, `reaped`, `refused`) |
| `Wdc.Cube.Blazor` | `wdc.cube.applications.live`: the Cube applications alive in the host's circuits |

Shopping adds `shopping.signins` by `outcome` (`ok`, `refused`, `throttled`) on its meter `Shopping`. It exports them
all with OpenTelemetry, along with ASP.NET's own meters and the runtime's, but only when `OTEL_EXPORTER_OTLP_ENDPOINT`
is set:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName, serviceInstanceId: Environment.MachineName))
    .WithMetrics(m => m
        .AddMeter(ShoppingMetrics.MeterName, RestServerMetrics.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
```

The standard OpenTelemetry variables configure the exporter:
- `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_PROTOCOL` say where the metrics go and how;
- `OTEL_METRIC_EXPORT_INTERVAL` sets how often they are sent.

The compose setup sends them straight to Prometheus, whose OTLP receiver is on (`--web.enable-otlp-receiver`). So no
collector is needed, and no `/metrics` route is exposed. Each series carries the service name as `job` and the
instance as `instance`, and the names follow Prometheus's conventions: `wdc_remote_transactions_total`,
`shopping_signins_total`, `http_server_request_duration_seconds`.

## The compose setup

[`samples/Shopping/docker`](https://github.com/mrcdom/wdc-cube-csharp/tree/main/samples/Shopping/docker) runs
everything above:
- PostgreSQL;
- two instances of `Shopping.Server`, the REST API with the WebAssembly app;
- two instances of `Shopping.Blazor`;
- nginx in front of each pair, with the two routing rules and the `/api/auth` limit per address;
- Prometheus.

Its README says how to start it. `check.cs` then checks it from outside:
- every instance is ready;
- a challenge from one instance is answered on the other;
- the token works on both, and a refresh token is spent on the other;
- each API client's calls stay on one instance, and the clients spread over both;
- each browser stays on one Blazor instance, and the browsers spread over both;
- nginx limits `/api/auth`;
- Prometheus has the metrics of the four instances.

When an instance stops, what it held in memory is gone:
- **its circuits:** a tab whose circuit is gone reloads on another instance and starts its application again from
  the address, still signed in;
- **its open remote transactions:** they are rolled back, and their clients get 410 on the next call.

## What to change for a real deployment

- **Limit requests per address at the proxy**, where every instance's traffic passes, as the compose setup's
  `nginx.conf` does with `limit_req`. Behind a proxy, every client reaches the instances from the proxy's address, so
  their own per-address limit would count all clients together. Turn it off (`Shopping:Auth:RequestsPerMinute` = 0),
  or configure [forwarded headers](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer) so the
  instances see the client's address.
- **TLS** at the proxy, and HTTPS redirection and HSTS in the hosts.
- **Keys from a secret store**, not from a file next to the compose setup.
- **Encrypt the key ring at rest** (`ProtectKeysWith…`), or restrict who can read its table. The sample keeps it in
  the clear: see [SECURITY.md](https://github.com/mrcdom/wdc-cube-csharp/blob/main/SECURITY.md).

## See also

- [Remote](../concepts/remote.md): remote transactions and what the client sees.
- [REST](rest.md): headers, limits and the meters of the server.
- [Blazor](blazor.md): hosting a Cube application in Blazor Server.
- [A walk through Shopping](../samples/shopping.md).
