# 0016. Remote transactions held across requests

## Status

Accepted (2026-11-18).

## Context

In the Kotlin implementation a client can open a transaction on the server (`/api/tx/begin`), send several requests
with `X-Tx-Id`, and commit (`/api/tx/commit`). The connection and its locks stay held between requests, which makes
concurrency, expiry, ownership and lost answers part of the feature. Without it, a remote client loses the
transactional API it has locally: a use case that writes two entities atomically would have to become a dedicated
server endpoint.

## Decision

Keep remote transactions, so a use case written against `ITransactionService` runs locally or remotely with the same
demarcation ([0012](0012-ambient-transaction.md)).

**Server** (`Wdc.Rest.Server`):

- `RemoteTransactionCoordinator` holds one detached transaction per id (`IDetachedTransactionSource`, implemented by
  `Database`): a connection and a transaction not bound to any flow. For each request, the transaction is made ambient
  for the duration of that request.
- Requests to one transaction run one at a time; commit and rollback wait for the request in progress, never closing
  the connection under it.
- **Reads with `X-Tx-Id` take part in the transaction**, so they see its own writes (and do not block on them where
  readers wait for uncommitted rows).
- A request that fails inside marks the transaction rollback-only; a later commit rolls back and reports
  `rolledback`.
- Each transaction has an owner, by default the authenticated user plus `X-Client-Id`; another owner is refused (403).
- Limits: 128 open in total, 16 per owner (429 beyond); a reaper rolls back after 60 s idle or 10 min of life;
  outcomes are remembered for 5 min.
- Repeating an outcome is a no-op; the opposite outcome, or an unknown, expired or finished transaction, is a
  conflict (409), never a 500.
- A write without `X-Tx-Id` while its owner has a transaction open is refused (409, configurable): it is almost always
  a lost header.

**Client** (`Wdc.Rest.Client`):

- `RemoteTransactionService` keeps the open transaction in an `AsyncLocal`, like the local one; every call of the same
  `RestClient` in that flow carries `X-Tx-Id`.
- A commit whose answer is lost asks `/api/tx/status` before reporting anything.
- A commit that the server answered with `rolledback` throws `TransactionSystemException`.

## Alternatives considered

- **Only one transaction per request**, with composite use cases as service endpoints: simpler, but the remote client
  loses the local transactional API.
- **Postponing the feature.**

## Consequences

- An open remote transaction holds a database connection; the limits and the reaper are part of the feature, not
  tuning.
- Tests cover concurrent use, expiry, ownership and lost commit answers.

See [Transactions](../concepts/transactions.md#remote-transactions-over-http) and [Remote](../concepts/remote.md).
