# Shopping (C#)

The Shopping demo, as in the Kotlin version (`wdc-cube-kotlin`): login, a restricted home with the catalogue and the purchase
history, product, cart, purchase and receipt; plus an administration area where Admin and Manager correct a purchase
already made (an errata, kept with who, when and why).

| Project | What it is |
|---|---|
| `Shopping.Domain` | Entities, models, criteria, repository contracts, password hashing. |
| `Shopping.Persistence` | Mappings, SQL repositories, DDL for PostgreSQL and SQL Server, demo data (with product images). |
| `Shopping.Presentation` | Places, keys, presenters, scopes. No view, no database. |
| `Shopping.Presentation.Tests` | The presentation without UI, against a real database in a throwaway schema. |
| `Shopping.Blazor.Views` | The Blazor views, pages and CSS, shared by the two Blazor hosts. |
| `Shopping.Blazor` | The interactive-server host (one application per circuit), image endpoint, sealed addresses. |
| `Shopping.Wasm` | The WebAssembly host: the presentation in the browser, data and login through `Shopping.Server`. |
| `Shopping.Blazor.Tests` | The whole page on Blazor's renderer (bUnit) and the host in memory. |
| `Shopping.Server` | The backend over HTTP: repositories as REST routes, remote transactions, JWT login, images. |
| `Shopping.Client` | The repositories and the login through HTTP, for clients that run the presentation themselves. |
| `Shopping.Avalonia` | The desktop app: the same presentation drawn by Avalonia, talking to `Shopping.Server`. |
| `Shopping.Avalonia.Tests` | The desktop app headless (clicks, typing, frame captures) against the server in memory. |

## Run

```bash
cd samples/Shopping/Shopping.Blazor
dotnet run        # Development, http://localhost:5190
```

The database comes from `appsettings.json` (`Shopping:Database`: `Provider` = `PostgreSql` or `SqlServer`,
`ConnectionString`, `Schema`). On the first start the schema and the demo data are created. Users (password = login):

| User | Role |
|---|---|
| `fulano`, `beotrano` | Customer: buys, sees their own purchases. |
| `gerente` | Manager: maintains products, corrects purchases. |
| `admin` | Admin: everything a role of the application may do. |
| `suporte` | Super: maintenance; some business rules are relaxed for it. |

The product images are illustrations made for this sample.

### WebAssembly

```bash
cd samples/Shopping/Shopping.Server
dotnet run -f net11.0          # serves the WebAssembly app at http://localhost:5200 and the API under /api
```

Addresses stay plain there: WebAssembly has no AES, so the sealed-address codec is off.

### Sealed addresses

After login, the Blazor Server app seals the addresses with a key per user (`home/product?_e=…`), derived from
`Shopping:History:Key` (Base64, 32 bytes; random in Development, so links die with the process). The same setting
exists on `Shopping.Server`, which hands each user's key out at login.

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
source scripts/test-env.sh   # database connection strings
dotnet test --solution wdc-cube.slnx
```
