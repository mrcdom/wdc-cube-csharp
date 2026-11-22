# 0015. An HTTP contract compatible with the Kotlin implementation

## Status

Accepted (2026-11-18).

## Context

The repository and transaction contracts need a second implementation over HTTP, so that a WebAssembly or desktop
client runs the same presentation code as a server-side host. The Kotlin implementation of Cube already defines such a
wire: routes per repository operation, JSON bodies for criteria, projections and slices, error bodies and headers.
.NET has its own conventions (ProblemDetails, OpenAPI-first APIs).

## Decision

Keep the Kotlin wire, so a client of one implementation can talk to a server of the other.

- **Routes** under a base path per repository (`/api/repo/<entity>` by convention): `insert`, `update`, `delete`,
  `count`, `fetch`, `fetch-page`, `fetch-by-id` (POST), and `GET {id}`. Remote transactions under `/api/tx`: `begin`,
  `commit`, `rollback`, `status`.
- **Bodies**: the same JSON for criteria, projections and slices (`offset`/`limit`, `page`/`pageSize`), written and
  read from the shared entity model ([0009](0009-shared-entity-model.md)) by `RepositoryWire`.
- **Errors** as `{"error":"<message>"}` with the same statuses: 400, 401, 403, 404, 409, 429, 500 (detail logged, not
  sent). `RestErrors` maps exceptions to statuses on the server and back on the client.
- **Headers**: `X-Tx-Id` (the remote transaction), `X-Client-Id` (the client instance), `Authorization: Bearer`.
- **Hardening that does not change the wire**:
  - a restriction of reach is combined with AND outside the caller's own request on that field, so it cannot be
    widened;
  - `update` also goes through the reach;
  - ceilings on `limit` and `pageSize`, and a fetch with no limit that matches too many rows is refused rather than
    cut;
  - a `delete` with no filter, and a criterion that cannot be a filter (`GT null`, `BETWEEN` with one value), are
    refused with 400;
  - transaction errors answer with conflict statuses instead of 500.
- The framework carries a bearer token (`RestClientOptions.AccessToken`, `RefreshAccess` after a 401, repeated once)
  but does not prescribe how it is issued: authentication is the application's.

## Alternatives considered

- **ProblemDetails (RFC 9457) and native OpenAPI**: more idiomatic in .NET, but breaks Kotlin and TypeScript clients.

## Consequences

- `Wdc.Rest` (shared wire), `Wdc.Rest.Server` (ASP.NET Core endpoints) and `Wdc.Rest.Client` (`RestClient`,
  `RemoteRepository`, `RemoteTransactionService`).
- Repository and transaction routes interoperate with the Kotlin implementation. Login does not necessarily: the
  Shopping sample, for example, uses a challenge login over salted password hashes that differs from the Kotlin
  sample's.
- The whole repository contract runs through the routes in the test suite, on both databases.

See [Remote](../concepts/remote.md).
