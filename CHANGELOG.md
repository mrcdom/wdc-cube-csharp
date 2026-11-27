# Changelog

The versions of the `Wdc.*` packages. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/): before 1.0, a minor version may break the API, and
says so. The samples follow `main` and have no versions of their own.

Released versions are immutable: a fix goes out in a new version.

## [Unreleased]

The first version, not yet published.

- **`Wdc.Cube`:** places in a tree declared by keys classes, flip intents and addresses, presenters and scopes,
  batched updates with a render handshake, a serial dispatcher per application, guarded actions that run one at a
  time (a failure goes to the presenter's `OnActionFailure`, then to the application), history with Back/Forward and
  deep links.
- **`Wdc.Cube.Blazor`, `Wdc.Cube.Avalonia`:** views for Blazor (Server and WebAssembly) and Avalonia.
- **`Wdc.Cube.Codec`:** sealed addresses (AES-SIV), compatible with the TypeScript implementation.
- **`Wdc.Cube.Testing`:** driving an application in tests, without a view.
- **`Wdc.Domain`:** entity models, criteria, projections, repository and transaction contracts, protected
  repositories (rules per role), resource calls (a write opens its own transaction), alternative groups in criteria (`anyOf`, server-side
  only: refused on the wire), declarative data policies per role (`DataPolicy`: rows per grant, readable and masked
  fields, writable fields, deletes), applied by `ProtectedRepository` in every host and to every child written in
  cascade.
- **`Wdc.Persistence`, `.PostgreSql`, `.SqlServer`:** SQL repositories built from mappings, one statement per fetch,
  writes in cascade; PostgreSQL and SQL Server 2025. `FilterContext.AnyOf` and `TranslateAny` (a term over several
  columns).
- **`Wdc.Rest`, `.Server`, `.Client`:** repositories and remote transactions over HTTP, with the wire of the Kotlin
  implementation. Security and cache headers on every answer, `Server-Timing`, a pluggable error map, 429 with
  `Retry-After` (`TooManyRequestsException`, also for ASP.NET's rate limiter), and metrics of the remote transactions.
- **`Wdc.Rest.Testing`:** a REST server in memory for tests, its client and raw requests.
- **Metrics:** `Wdc.Rest.Server` and `Wdc.Cube.Blazor` publish their instruments on `System.Diagnostics.Metrics`.

[Unreleased]: https://github.com/mrcdom/wdc-cube-csharp/commits/main
