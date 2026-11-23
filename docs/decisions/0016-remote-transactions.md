# 0016. Remote transactions held across requests

## Status

Accepted (2026-11-18). Amended 2026-11-22: concurrent use and doomed commits are refused (409), a
transaction that no longer exists is gone (410), as in the Kotlin implementation.

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
- Requests to one transaction run one at a time: a request or a commit while another request runs is refused (409)
  rather than queued, so the server holds no waiting work. A rollback then holds at once for every later call, and
  the connection is rolled back when the running request ends, never under it.
- **Reads with `X-Tx-Id` take part in the transaction**, so they see its own writes (and do not block on them where
  readers wait for uncommitted rows).
- A request that fails inside dooms the transaction; a later commit rolls back and is refused (409), so no client can
  take it for a success by looking only at the status code.
- Each transaction has an owner, by default the authenticated user plus `X-Client-Id`; another owner is refused (403).
- Limits: 128 open in total, 16 per user across its clients (429 beyond); a reaper rolls back after 60 s idle or 10 min of life;
  outcomes are remembered for 5 min.
- Repeating an outcome answers the same; the opposite outcome is a conflict (409); an unknown, expired or finished
  transaction is gone (410). Never a 500.
- A write without `X-Tx-Id` while its owner has a transaction open is refused (409, configurable): it is almost always
  a lost header.

**Client** (`Wdc.Rest.Client`):

- `RemoteTransactionService` keeps the open transaction in an `AsyncLocal`, like the local one; every call of the same
  `RestClient` in that flow carries `X-Tx-Id`.
- A commit whose answer is lost asks `/api/tx/status` before reporting anything.
- A commit refused because a request inside failed (409), or answered `rolledback` by `/status`, throws
  `TransactionSystemException`.

## Alternatives considered

- **Only one transaction per request**, with composite use cases as service endpoints: simpler, but the remote client
  loses the local transactional API.
- **Postponing the feature.**
- **Queuing concurrent requests on the server** instead of refusing them: more tolerant, but it holds waiting work
  and connections on the server for what is a client error (one transaction used by two calls at once).
- **Answering a doomed commit with 200 `rolledback`**: correct for a client that reads the body, a silent false
  success for one that only reads the status code.

## Consequences

- An open remote transaction holds a database connection; the limits and the reaper are part of the feature, not
  tuning.
- Tests cover concurrent use, expiry, ownership and lost commit answers.

See [Transactions](../concepts/transactions.md#remote-transactions-over-http) and [Remote](../concepts/remote.md).
