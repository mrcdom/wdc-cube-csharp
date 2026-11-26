# 0001. `Wdc.*` namespace and package prefix

## Status

Accepted (2026-11-18).

## Context

Cube is a family of implementations (TypeScript, Kotlin, Rust, PHP and this one). The .NET implementation ships as
several NuGet packages that an application references selectively: the presentation core, one package per renderer,
the domain contracts, the SQL engine and one package per database, the HTTP layer. The packages need one prefix that
is short, unlikely to collide on NuGet, and that reads the same in `using` directives and package names.

## Decision

Every namespace and package starts with `Wdc.`, and the project name is the root namespace
(`RootNamespace = $(MSBuildProjectName)`):

| Area | Packages |
|---|---|
| Presentation | `Wdc.Cube`, `Wdc.Cube.Blazor`, `Wdc.Cube.Avalonia`, `Wdc.Cube.Codec`, `Wdc.Cube.Testing` |
| Domain | `Wdc.Domain` |
| Persistence | `Wdc.Persistence`, `Wdc.Persistence.PostgreSql`, `Wdc.Persistence.SqlServer` |
| HTTP | `Wdc.Rest`, `Wdc.Rest.Server`, `Wdc.Rest.Client`, `Wdc.Rest.Testing` |

- `Wdc.Cube.*` is the presentation side (places, presenters, scopes, renderers).
- `Wdc.Domain` and below carry no dependency on `Wdc.Cube`: a server can use the data stack without the presentation.
- Optional capabilities (a renderer, a database, the address codec) are separate packages, so an application only
  pulls the dependencies it uses.

## Alternatives considered

- **A single package**: simpler to reference, but every application would carry ASP.NET Core, a UI toolkit and two
  database drivers.

## Consequences

- Package names are stable public API; renaming one is a breaking change.
- A new database or renderer is a new `Wdc.Persistence.<Db>` or `Wdc.Cube.<Renderer>` package, not a change to the
  core ones.

See [Architecture](../concepts/architecture.md) and the package table in the [README](https://github.com/mrcdom/wdc-cube-csharp/blob/main/README.md).
