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

## Run

```bash
cd samples/Shopping/Shopping.Blazor
dotnet run        # Development, http://localhost:5190
```

The database comes from `appsettings.json` (`Shopping:Database`: `Provider` = `PostgreSql` or `SqlServer`,
`ConnectionString`, `Schema`). On the first start the schema and the demo data are created. Users: `admin`/`admin`,
`fulano`/`fulano`, `beotrano`/`beotrano`.

## Tests

```bash
source scripts/test-env.sh   # database connection strings (decision D-14)
dotnet test --solution wdc-cube.slnx
```
