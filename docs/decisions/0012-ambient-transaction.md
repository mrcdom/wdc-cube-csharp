# 0012. Ambient transaction via `AsyncLocal`

## Status

Accepted (2026-11-18). Amended 2026-11-22: a failing participant dooms the transaction.

## Context

A unit of work is demarcated by whoever knows it (a use case), not by repositories, and repositories should find the
open transaction without an extra parameter on every call, as in JPA. The same use case must run against a local
database and against a server over HTTP ([0016](0016-remote-transactions.md)). `System.Transactions` brings
distributed-transaction semantics and promotion rules that a single-connection ADO.NET unit of work does not need.

## Decision

- `ITransactionService` offers the propagations `Required`, `RequiresNew`, `Mandatory`, `Supports`, `NotSupported` and
  `Never`, plus `AfterCommit`. The work receives an `ITransactionContext` (`IsActive`, `SetRollbackOnly`,
  `IsRollbackOnly`).
- The open transaction lives in an `AsyncLocal<T>`, which follows every `await`; repositories at any depth join it.
- Transactions are the framework's own owner/participant scopes over `DbConnection`/`DbTransaction`, not
  `System.Transactions`.
- A write (insert, update, delete) with no transaction open always runs in an implicit `Required`, decided by the
  persistence layer itself for any caller, so a multi-statement write is atomic. A read with no transaction open uses
  a connection of its own, in autocommit.
- **Outcome for the scope that opened the transaction:**
  - a normal return commits;
  - an explicit `SetRollbackOnly()` rolls back **silently**: it is the code's choice, not a failure;
  - an exception rolls back and is rethrown unchanged;
  - **a participant that threw dooms the transaction**, even if its caller caught the exception and carried on; at the
    end the owner rolls back and throws `TransactionSystemException` instead of returning the result as a success.
- The same rules hold for the remote transaction service: when a request inside a remote transaction failed, the
  server rolls back at commit and the client throws `TransactionSystemException`.
- Work that must survive a possibly failing step runs that step in `RequiresNew`.

## Alternatives considered

- **`System.Transactions` / `TransactionScope`**: its promotion and distributed semantics are not needed for one
  connection per unit of work; the owner/participant model is the one the Kotlin implementation already uses.
- **Implicit transaction decided per HTTP request** (the Kotlin approach): leaves local callers without atomic writes.
- **Rolling back a doomed transaction silently**: the caller would take as written what was undone.

## Consequences

- One transaction is one connection: statements of a unit of work run one after another, never in parallel.
- Catching an exception inside a transaction does not save the writes made before it.
- `AfterCommit` tasks run only after a real commit, outside the transaction; their failures are logged and never
  change the outcome.
- Transactions that outlive a call (detached transactions) are a separate, explicit API for the remote coordinator.

See [Transactions](../concepts/transactions.md).
