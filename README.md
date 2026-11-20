# wdc-cube-csharp

**Cube** is a front-end architecture that keeps application logic out of the view layer. This is its C#/.NET
implementation, alongside the [TypeScript](https://github.com/mrcdom/wdc-cube) and
[Kotlin](https://github.com/mrcdom/wdc-cube-kotlin) ones.

Screens are described by three collaborating pieces:

| Piece | Role |
|---|---|
| **Place** | A node in the navigation tree. Places form a hierarchy, and an address is a path through it. |
| **Presenter** | Owns the logic of a place: loads data, reacts to user actions, decides where to navigate. Knows nothing about the view technology. |
| **Scope** | The state a presenter publishes for a view to draw. Changes are batched and flushed to the view. |

Navigation happens through a **FlipIntent**: a description of "go to this place with these parameters" that maps
one-to-one to the address. A presenter never touches the router; it fills an intent and flips to it.

Because presenters and scopes are plain C#, the whole application is tested without any UI, and each view technology
(Blazor, Avalonia) is a thin layer that draws scopes and forwards user events back to them.

## Packages

| Project | What it is |
|---|---|
| `Wdc.Cube` | Places, intents, navigation, presenters, scopes, the update pipeline, history. |
| `Wdc.Cube.Testing` | Driving an application in tests, without a view. |
| `Wdc.Cube.Blazor` | Views for Blazor (Server and WebAssembly). |
| `Wdc.Cube.Avalonia` | Views for Avalonia (desktop). |
| `Wdc.Cube.Codec` | Sealed addresses (AES-SIV), compatible with the TypeScript version. |
| `Wdc.Domain` | Entity models, criteria, projections, repository and transaction contracts, protected repositories (rules per role). |
| `Wdc.Persistence` | SQL repositories built from mappings: one statement per fetch, writes in cascade. |
| `Wdc.Persistence.PostgreSql`, `Wdc.Persistence.SqlServer` | The two dialects. |
| `Wdc.Rest`, `Wdc.Rest.Server`, `Wdc.Rest.Client` | Repositories and transactions over HTTP: the same contracts, local or remote. |

The libraries are trimming- and AOT-compatible: no runtime reflection in the core and the domain.

## Samples

- [`samples/Tutorial`](samples/Tutorial/README.md): a page frame, a restricted area, a list with a detail dialog and
  TodoMVC, in Blazor.
- [`samples/Shopping`](samples/Shopping/README.md): login, catalogue, cart, purchase, receipt and an administration
  area, on PostgreSQL or SQL Server; in Blazor Server, Blazor WebAssembly and Avalonia, locally or through its server.

## Build and test

Requires the .NET 11 SDK (see `global.json`); the libraries target `net10.0` and `net11.0`.

```bash
dotnet build wdc-cube.slnx
source scripts/test-env.sh      # optional: database connection strings for the integration tests
dotnet test --solution wdc-cube.slnx
```

Tests that need a database are skipped, with the reason, when its connection string is missing. See
`scripts/test-env.sh` for PostgreSQL and SQL Server.

## License

[MIT](LICENSE) © 2017-2026 WeDoCode Consultoria e Soluções Avançadas LTDA
