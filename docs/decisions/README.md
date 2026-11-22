# Architecture decisions

These are the Architecture Decision Records (ADRs) of the framework: the structural choices behind Cube for .NET,
with the context that led to each, the alternatives that were weighed, and what follows from it. They describe what
the code does today; when a decision is revised, its record is updated (or a new one supersedes it) rather than left
to drift.

The records cover the framework only. Choices made inside a sample application appear here at most as examples.

Each record has the same sections: **Status**, **Context**, **Decision**, **Alternatives considered** and
**Consequences**, and links to the [concept](../README.md#concepts) that explains the mechanism in detail.

| # | Decision | Status |
|---|---|---|
| [0001](0001-wdc-namespace-and-packages.md) | `Wdc.*` namespace and package prefix | Accepted |
| [0002](0002-target-frameworks.md) | Target frameworks: `net10.0` and `net11.0`, C# 14 | Accepted |
| [0003](0003-serial-dispatcher-per-application.md) | One serial presentation dispatcher per application | Accepted |
| [0004](0004-transactional-navigation.md) | Transactional navigation and the meaning of `false` | Accepted |
| [0005](0005-concurrent-navigation-requests.md) | Concurrent navigation requests | Accepted |
| [0006](0006-scope-render-handshake.md) | Scopes with an explicit render handshake | Accepted |
| [0007](0007-renderers-blazor-and-avalonia.md) | Renderers: Blazor first, Avalonia second | Accepted |
| [0008](0008-aes-siv-address-codec.md) | Sealed addresses with AES-SIV, built on the platform's AES | Accepted |
| [0009](0009-shared-entity-model.md) | One entity model for SQL and JSON | Accepted |
| [0010](0010-own-sql-ast-over-ado-net.md) | A SQL engine on its own AST over ADO.NET | Accepted |
| [0011](0011-supported-databases.md) | Supported databases: PostgreSQL and SQL Server | Accepted |
| [0012](0012-ambient-transaction.md) | Ambient transaction via `AsyncLocal` | Accepted (amended) |
| [0013](0013-writes-in-cascade.md) | Writes in cascade, guided by the projection | Accepted |
| [0014](0014-rules-per-role-in-protected-repository.md) | Rules per role, in the protected repository | Accepted |
| [0015](0015-http-contract-compatible-with-kotlin.md) | An HTTP contract compatible with the Kotlin implementation | Accepted |
| [0016](0016-remote-transactions.md) | Remote transactions held across requests | Accepted |

## Reading order

- **Foundations:** 0001–0002.
- **Presentation:** 0003–0008, in the order a navigation meets them: the dispatcher it runs on, the walk, concurrent
  requests, how scopes reach the screen, the renderers, and sealed addresses.
- **Data:** 0009–0014, from the model shared by every layer to the SQL engine, transactions, cascades and the rules
  per role.
- **Remote:** 0015–0016, the same contracts over HTTP.

## Adding a record

Number it after the last one, keep it short (the concept documents hold the details), and date its status. A record
that no longer holds is marked **Superseded by NNNN** instead of being deleted.
