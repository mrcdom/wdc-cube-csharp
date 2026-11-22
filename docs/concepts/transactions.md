# Transactions

A unit of work is demarcated by whoever knows it, typically a use case, through `ITransactionService`. Repositories never open or close transactions: they find the open one in the calling flow and join it. The same interface is implemented by the local `Database` on the server and by `RemoteTransactionService` on a client that talks to the server over HTTP, so a use case that writes a purchase and its stock movement in one transaction runs unchanged on either side.

## `ITransactionService`

```csharp
public interface ITransactionService
{
    Task<T> Execute<T>(Propagation propagation, Func<ITransactionContext, Task<T>> work,
        CancellationToken cancellationToken = default);

    Task AfterCommit(Func<Task> task);
}
```

`TransactionServiceExtensions` turns each propagation into a method: `Required`, `RequiresNew`, `Mandatory`, `Supports`, `NotSupported` and `Never` (with a result), plus `Required` and `RequiresNew` overloads for work that returns no result.

```csharp
public async Task<long> Checkout(Purchase purchase, CancellationToken ct)
{
    return await transactions.Required(async _ =>
    {
        await purchases.Insert(purchase, PurchaseWrite, ct);
        await transactions.AfterCommit(() => notifier.PurchaseMade(purchase.Id!.Value));
        return purchase.Id!.Value;
    }, ct);
}
```

The work receives an `ITransactionContext`:

- `IsActive`: whether a physical transaction is open (false under `Supports` without one, `NotSupported` and `Never`).
- `SetRollbackOnly()`: undo the transaction at the end, without throwing. It throws when there is no transaction.
- `IsRollbackOnly`.

## The ambient transaction

- The open transaction is kept in an `AsyncLocal`. It follows every `await` inside the work, so repositories called at any depth find it without a parameter.
- A change made inside `Execute` is scoped to that flow: when `Execute` returns, the caller sees its own transaction (or none) again.
- One transaction is one connection. Run the statements of a unit of work one after the other; do not start them in parallel (`Task.WhenAll`) inside a transaction.
- `Database.InTransaction` tells whether the calling flow is inside a transaction of that database.

## Propagation

| Propagation | Transaction open | No transaction open |
|---|---|---|
| `Required` | joins it | opens one |
| `RequiresNew` | suspends it and opens its own | opens one |
| `Mandatory` | joins it | throws `TransactionSystemException` |
| `Supports` | joins it | runs without one |
| `NotSupported` | suspends it and runs without one | runs without one |
| `Never` | throws `TransactionSystemException` | runs without one |

`Required` and `RequiresNew` are the ones you will use. `RequiresNew` commits independently: work done in it stays committed even when the outer transaction later rolls back. A suspended transaction is invisible to the inner work, which reads with its own connection.

## Commit and rollback

For the scope that opened the transaction:

- A normal return commits, unless the transaction is rollback-only, in which case it rolls back without throwing.
- Any exception rolls back and is rethrown unchanged (the same instance).
- A normal return after a call inside the transaction failed (a participant, a statement) rolls back and throws
  `TransactionSystemException`: the work did not happen, even if its exception was caught.
- A failure to open the connection, begin or commit surfaces as `TransactionSystemException`, with the cause as inner exception. A failure while rolling back is logged and does not hide the original exception.

For a scope that joined an open transaction (`Required`, `Mandatory`, `Supports` inside one):

- It neither commits nor rolls back; the outcome belongs to the scope that opened the transaction.
- `SetRollbackOnly()` in a participant makes the whole transaction roll back at the end.
- An exception thrown by a participant, or by a statement run in the transaction, dooms it: `IsRollbackOnly` becomes
  true, and the scope that opened it rolls back. Catching the exception does not save the writes made before it. To
  try something that may fail without losing the rest, run it in `RequiresNew`.

## After commit

`AfterCommit(task)` schedules work that must happen only if the data is really committed (a notification, an e-mail):

- Inside a transaction, the task is queued on the transaction that is open in the flow (for `RequiresNew`, its own) and run after it commits, in scheduling order, outside the transaction, with the connection already returned.
- On rollback the queued tasks are dropped.
- With no transaction open, the task runs immediately, and the returned `Task` completes when it ends.
- A failing task is logged (on the client, through the logger given to `RemoteTransactionService`) and never changes
  the outcome of the transaction, nor reaches the caller.

## Reads and writes inside and outside a transaction

`SqlRepository` and `Database.Execute`/`Database.Scalar` follow these rules:

- Inside a transaction, reads and writes use the transaction's connection, so reads see the transaction's own uncommitted writes.
- A read outside a transaction uses a connection of its own, in autocommit, released after the statement.
- A write outside a transaction runs in one of its own (implicit `Required`). A repository write that takes several statements (an insert followed by its read-back, a cascade into children) is therefore always atomic.
- Whether other connections see uncommitted rows, or wait for them, is the database's isolation behavior: SQL Server's default read committed blocks readers unless `READ_COMMITTED_SNAPSHOT` is on.

## Detached transactions

Some transactions must outlive the call that opened them: a remote client begins one, sends several requests, and commits later. `Database` implements `IDetachedTransactionSource` for that:

```csharp
await using IDetachedTransaction tx = await database.BeginDetached(ct);

await tx.Run(() => purchases.Insert(purchase, cancellationToken: ct));   // request 1
await tx.Run(() => items.Update(item, projection: amountOnly, cancellationToken: ct)); // request 2

bool committed = await tx.Commit(ct);
```

- `BeginDetached` opens a connection and a transaction not bound to the caller's flow.
- `Run(work)` makes it the ambient transaction for the duration of `work`; repositories and `ITransactionService` calls inside join it. Only one `Run` may be in progress at a time: another one, or a `Commit`, throws `TransactionConflictException` meanwhile. A `Rollback` asked during a `Run` (a timeout, a disposal) happens when that `Run` ends.
- A `Run` that throws makes the transaction rollback-only.
- `Commit` commits, or rolls back when rollback-only, releases the connection, runs the after-commit tasks, and returns whether it committed.
- `Rollback` rolls back and releases the connection. `DisposeAsync` rolls back if nothing finished the transaction.
- After it has finished, `Run` and a second `Commit` throw `TransactionConflictException`; a second `Rollback` does nothing.

## Remote transactions over HTTP

On a client, `RemoteTransactionService` implements `ITransactionService` over the server's remote transactions. A use case written against `ITransactionService` keeps the same code:

- Opening a transaction calls `POST /api/tx/begin` and keeps the returned id in an `AsyncLocal` of the `RestClient`. Every call of that client in the flow carries it in the `X-Tx-Id` header, reads included, so they see the transaction's own writes.
- Normal return commits (`POST /api/tx/commit`); an exception rolls back (`POST /api/tx/rollback`) and is rethrown unchanged.
- If a request inside the transaction failed, the server has marked it rollback-only; the commit then rolls back and the client throws `TransactionSystemException` instead of reporting success.
- If the answer to a commit is lost, the client asks `GET /api/tx/status` before reporting anything.

On the server, `RemoteTransactionCoordinator` holds the open transactions, each a detached transaction from an `IDetachedTransactionSource` (the `Database`):

- Each transaction has an owner (by default the authenticated user and the client instance). A request with another owner's id is refused with `AccessDeniedException`.
- Requests to one transaction run one at a time: a request or a commit while another request runs is refused (`TransactionConflictException`, HTTP 409); a rollback then holds at once, and the connection rolls back when the running request ends.
- `RemoteTransactionOptions` sets the limits: `MaxOpen` (128) in total, `MaxOpenPerOwner` (16), `IdleTimeout` (60 s), `MaxLifetime` (10 min), `OutcomeRetention` (5 min) and `ReapInterval` (5 s). Over a limit, `Begin` throws `TransactionLimitExceededException` (HTTP 429).
- `RemoteTransactionReaper`, a hosted service, rolls back transactions that are idle or too old.
- An unknown, expired or finished transaction is a `TransactionNotFoundException` (HTTP 410). A commit of a transaction doomed by a failed request rolls back and is refused (409). Repeating an outcome answers the same.
- A write without `X-Tx-Id` while its owner has a transaction open is refused with 409 (`RestServerOptions.RefuseWritesOutsideOpenTransaction`, on by default): it is almost always a lost header.

Endpoints, wiring and error mapping are described in [remote](remote.md).

## See also

- [Architecture](architecture.md)
- [Update cycle](update-cycle.md)
- [Dispatcher](dispatcher.md)
- [Navigation](navigation.md)
- [History](history.md)
- [Domain model](domain.md)
- [Persistence](persistence.md)
- [Protected repositories](protected-repositories.md)
- [Remote repositories and transactions](remote.md)
- [Getting started](../getting-started.md)
