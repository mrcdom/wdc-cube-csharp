# Changelog

The versions of the `Wdc.*` packages. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/): before 1.0, a minor version may break the API, and
says so. The samples follow `main` and have no versions of their own.

Released versions are immutable: a fix goes out in a new version.

## [Unreleased]

The first version, not yet published.

- **`Wdc.Cube`:** places in a tree declared by keys classes, flip intents and addresses, presenters and scopes,
  batched updates with a render handshake, a serial dispatcher per application, guarded actions that run one at a
  time, history with Back/Forward and deep links.
- **`Wdc.Cube.Blazor`, `Wdc.Cube.Avalonia`:** views for Blazor (Server and WebAssembly) and Avalonia.
- **`Wdc.Cube.Codec`:** sealed addresses (AES-SIV), compatible with the TypeScript implementation.
- **`Wdc.Cube.Testing`:** driving an application in tests, without a view.
- **`Wdc.Domain`:** entity models, criteria, projections, repository and transaction contracts, protected
  repositories (rules per role).
- **`Wdc.Persistence`, `.PostgreSql`, `.SqlServer`:** SQL repositories built from mappings, one statement per fetch,
  writes in cascade; PostgreSQL and SQL Server 2025.
- **`Wdc.Rest`, `.Server`, `.Client`:** repositories and remote transactions over HTTP, with the wire of the Kotlin
  implementation.

[Unreleased]: https://github.com/mrcdom/wdc-cube-csharp/commits/main
