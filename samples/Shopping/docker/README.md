# Shopping with several instances

The Shopping demo behind one address per host, with two instances of each. This is the setup that
[Running several instances](../../../docs/guides/several-instances.md) describes.

| Service | What it is | On the host |
|---|---|---|
| `nginx` | The REST API with the WebAssembly app, and Blazor Server, each in front of its two instances | <http://localhost:5080>, <http://localhost:5090> |
| `server-a`, `server-b` | `Shopping.Server` | 5081, 5082 |
| `blazor-a`, `blazor-b` | `Shopping.Blazor` | 5091, 5092 |
| `postgres` | The database: the first instance creates the tables and the demo data | — |
| `prometheus` | Receives every instance's metrics over OTLP | <http://localhost:9090> |

## Run

Docker with Compose, and the .NET SDK for the check:

```bash
cd samples/Shopping/docker
cp .env.example .env          # then fill it in: a password and two keys (openssl rand -base64 32)
docker compose up -d --build --wait
```

Sign in at <http://localhost:5090> (Blazor Server) or <http://localhost:5080> (WebAssembly) with the demo users of
the [sample's README](../README.md). Each nginx answer carries `X-Upstream`, the instance that served it.

## Check

```bash
dotnet build check.cs && dotnet run --no-build check.cs
```

It checks, from outside:
1. Every instance, and nginx in front of each pair, is ready.
2. A sign-in challenge from one instance is answered on the other. The token works on both, and the refresh token
   is spent on the other.
3. Through nginx, each API client's calls, including its remote transactions, stay on one instance. The clients
   spread over both instances.
4. Through nginx, each browser's page and circuit calls stay on one Blazor instance. The browsers spread over both
   instances.
5. nginx answers 429 to a burst on `/api/auth`.
6. Prometheus has the metrics of the four instances, the remote transactions and the sign-ins.

The CI's `compose` job runs it on every push.

## Stop

```bash
docker compose down -v        # -v also drops the database
```

The images are pinned:
- `mcr.microsoft.com/dotnet/sdk:11.0.100-resolute` and `mcr.microsoft.com/dotnet/aspnet:11.0.0-resolute`;
- `postgres:17.12`;
- `nginx:1.30.6`;
- `prom/prometheus:v3.16.0`.
