# Shopping (C#)

Port of the Kotlin Shopping demo (`wdc-cube-kotlin`): login, a restricted home with the catalogue and the purchase
history, product, cart, purchase and receipt. Choices and differences: `.claude/docs/decisions/D-15-port-do-shopping.md`.

| Project | What it is |
|---|---|
| `Shopping.Domain` | Entities, models, criteria, repository contracts, password hashing. |
| `Shopping.Persistence` | Mappings, SQL repositories, DDL for PostgreSQL and SQL Server, demo data (with product images). |
| `Shopping.Presentation` | Places, keys, presenters, scopes. No view, no database. |
| `Shopping.Presentation.Tests` | The presentation without UI, against a real database in a throwaway schema. |
| `Shopping.Blazor` | The views and the host (interactive server: one application per circuit), image endpoint. |
| `Shopping.Blazor.Tests` | The whole page on Blazor's renderer (bUnit) and the host in memory. |
| `Shopping.Server` | The backend over HTTP (phase 5): repositories as REST routes, remote transactions, JWT login, images. |
| `Shopping.Client` | The repositories and the login through HTTP, for clients that run the presentation themselves. |
| `Shopping.Avalonia` | The desktop app: the same presentation drawn by Avalonia, talking to `Shopping.Server`. |
| `Shopping.Avalonia.Tests` | The desktop app headless (clicks, typing, frame captures) against the server in memory. |

## Run

```bash
cd samples/Shopping/Shopping.Blazor
dotnet run        # Development, http://localhost:5190
```

The database comes from `appsettings.json` (`Shopping:Database`: `Provider` = `PostgreSql` or `SqlServer`,
`ConnectionString`, `Schema`). On the first start the schema and the demo data are created. Users: `admin`/`admin`,
`fulano`/`fulano`, `beotrano`/`beotrano`.

### Desktop (Avalonia) over the server

```bash
cd samples/Shopping/Shopping.Server
dotnet run -f net11.0          # Development, http://localhost:5200 (same database settings as above)

cd samples/Shopping/Shopping.Avalonia
dotnet run                     # or: dotnet run -- --server http://host:port   (or SHOPPING_SERVER=...)
```

No navigation bar on the desktop; Alt+←/→ walk back and forward. In production the server needs `Shopping:Jwt:Key` (Base64, 32 bytes or
more); in Development a random key is used and the tokens die with the process.

The Shopping presentation tests run twice: on the database and through the server (`*HttpTests`), unchanged.

## Tests

```bash
source scripts/test-env.sh   # database connection strings (decision D-14)
dotnet test --solution wdc-cube.slnx
```
